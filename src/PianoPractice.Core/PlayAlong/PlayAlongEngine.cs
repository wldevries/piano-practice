using PianoPractice.Core.Theory;

namespace PianoPractice.Core.PlayAlong;

public enum PracticeHands
{
    Right,
    Left,
    Both,
    /// <summary>The piano plays everything; nothing to play.</summary>
    None,
}

public enum PlayAlongMode
{
    /// <summary>The song stops at each chord until all its notes are played.</summary>
    Wait,
    /// <summary>The song runs at tempo; notes are scored on timing.</summary>
    Flow,
}

public enum NoteStatus
{
    /// <summary>To be played by the player, not yet played.</summary>
    Upcoming,
    Hit,
    /// <summary>Flow mode: the note went by without being played.</summary>
    Missed,
    /// <summary>The other hand, played by the piano.</summary>
    Accompaniment,
    /// <summary>The other hand, muted.</summary>
    Silent,
    /// <summary>Outside the bar range being practised.</summary>
    OutOfRange,
}

public enum PressResult { Correct, Wrong }

/// <summary>A note the app should send to the piano.</summary>
public readonly record struct NoteAction(Pitch Pitch, int Velocity, bool IsOn, Hand Hand);

public sealed record PlayAlongSetup
{
    public PracticeHands Hands { get; init; } = PracticeHands.Both;
    public PlayAlongMode Mode { get; init; } = PlayAlongMode.Wait;

    /// <summary>True: the piano plays the hand you're not practising. False: it's only shown.</summary>
    public bool PlayOtherHand { get; init; } = true;

    /// <summary>Playback speed, 1 = the song's tempo.</summary>
    public double Tempo { get; init; } = 1;

    /// <summary>1-based, inclusive.</summary>
    public int FromBar { get; init; } = 1;

    /// <summary>1-based, inclusive; clamped to the last bar.</summary>
    public int ToBar { get; init; } = int.MaxValue;

    /// <summary>Start again at <see cref="FromBar"/> after <see cref="ToBar"/>.</summary>
    public bool Loop { get; init; }

    /// <summary>Real seconds of falling notes before the first note arrives.</summary>
    public double LeadInSeconds { get; init; } = 2;

    public bool Practises(Hand hand) => Hands switch
    {
        PracticeHands.Both => true,
        PracticeHands.Right => hand == Hand.Right,
        PracticeHands.Left => hand == Hand.Left,
        _ => false,
    };
}

public sealed record PlayAlongResult(int Chords, int CleanChords, int WrongNotes, int NotesHit, int NotesMissed, double MeanAbsTimingMs, int Passes)
{
    /// <summary>Wait mode: chords played without a wrong note. Flow mode: notes hit in time.</summary>
    public double Accuracy(PlayAlongMode mode) => mode == PlayAlongMode.Wait
        ? (Chords == 0 ? 0 : (double)CleanChords / Chords)
        : (NotesHit + NotesMissed == 0 ? 0 : (double)NotesHit / (NotesHit + NotesMissed));
}

/// <summary>
/// Runs a song for play-along: moves the song position, decides which notes the player should play
/// and which the piano plays, and scores what the player does. It has no clock of its own; the caller
/// passes the elapsed time to <see cref="Update"/>, so it is deterministic and testable.
/// </summary>
public sealed class PlayAlongEngine
{
    /// <summary>Notes that start this close together (song seconds) form one chord.</summary>
    private const double ChordTolerance = 0.03;

    /// <summary>Wait mode: how early (real seconds) a chord may be played before the song reaches it.</summary>
    public const double EarlyWindow = 0.25;

    /// <summary>Flow mode: how far off (real seconds) a note may be and still count.</summary>
    public const double TimingWindow = 0.2;

    private sealed class Chord(double start)
    {
        public double Start { get; } = start;
        public List<int> Notes { get; } = [];
        public HashSet<int> Pending { get; } = [];
        public bool Clean { get; set; } = true;
    }

    private readonly NoteStatus[] _status;
    private readonly List<Chord> _chords = [];
    private readonly List<int> _practised = []; // note indices, by start
    private readonly HashSet<int> _practisedSet = [];
    private readonly List<int> _accompaniment = []; // note indices, by start
    private readonly List<int> _sounding = []; // accompaniment notes currently on

    private int _nextChord;
    private int _nextAccompaniment;
    private int _nextToMiss;
    private double _tempo;

    private int _chordsDone, _cleanChords, _wrongNotes, _hits, _missed, _passes;
    private double _timingSumMs;

    public PlayAlongEngine(Song song, PlayAlongSetup setup)
    {
        Song = song;
        Setup = setup;
        _tempo = Math.Clamp(setup.Tempo, 0.1, 2);

        var lastBar = Math.Max(song.BarCount, 1);
        FromBar = Math.Clamp(setup.FromBar, 1, lastBar);
        ToBar = Math.Clamp(setup.ToBar, FromBar, lastBar);
        RangeStart = song.BarStart(FromBar);
        RangeEnd = song.BarStart(ToBar + 1);

        _status = new NoteStatus[song.Notes.Count];
        for (var i = 0; i < song.Notes.Count; i++)
        {
            var note = song.Notes[i];
            if (note.Start < RangeStart - 1e-6 || note.Start >= RangeEnd - 1e-6) _status[i] = NoteStatus.OutOfRange;
            else if (setup.Practises(note.Hand)) { _practised.Add(i); _practisedSet.Add(i); }
            else if (setup.PlayOtherHand) _accompaniment.Add(i);
        }

        foreach (var index in _practised)
        {
            var note = song.Notes[index];
            if (_chords.Count == 0 || note.Start - _chords[^1].Start > ChordTolerance) _chords.Add(new Chord(note.Start));
            _chords[^1].Notes.Add(index);
        }

        Reset();
    }

    public Song Song { get; }
    public PlayAlongSetup Setup { get; }
    public PlayAlongMode Mode => Setup.Mode;
    public int FromBar { get; }
    public int ToBar { get; }
    public double RangeStart { get; }
    public double RangeEnd { get; }

    /// <summary>Song position in seconds (at 100% tempo). Negative during the lead-in of the first bar.</summary>
    public double Position { get; private set; }

    /// <summary>Playback speed; can be changed while playing.</summary>
    public double Tempo
    {
        get => _tempo;
        set => _tempo = Math.Clamp(value, 0.1, 2);
    }

    /// <summary>Wait mode: the song is held until the current chord is played.</summary>
    public bool IsWaiting { get; private set; }

    public bool IsFinished { get; private set; }

    public NoteStatus StatusOf(int noteIndex) => _status[noteIndex];

    /// <summary>Total number of notes the player has to play per pass.</summary>
    public int PractisedNoteCount => _practised.Count;

    /// <summary>Notes the player should play next (wait mode: the remaining notes of the next chord).</summary>
    public IReadOnlyCollection<Pitch> Expected => Mode == PlayAlongMode.Wait && _nextChord < _chords.Count
        ? _chords[_nextChord].Pending.Select(m => new Pitch(m)).ToList()
        : [];

    /// <summary>The song notes behind <see cref="Expected"/>, e.g. to colour them by hand.</summary>
    public IReadOnlyList<SongNote> ExpectedNotes => Mode == PlayAlongMode.Wait && _nextChord < _chords.Count
        ? _chords[_nextChord].Notes.Select(i => Song.Notes[i]).Where(n => _chords[_nextChord].Pending.Contains(n.Pitch.MidiNumber)).ToList()
        : [];

    /// <summary>Song time of the next chord to play (wait mode), or null.</summary>
    public double? NextChordStart => Mode == PlayAlongMode.Wait && _nextChord < _chords.Count ? _chords[_nextChord].Start : null;

    /// <summary>The bar the song position is in (1-based).</summary>
    public int CurrentBar => Song.BarAt(Math.Max(Position, RangeStart));

    public PlayAlongResult Result => new(
        _chordsDone, _cleanChords, _wrongNotes, _hits, _missed,
        _hits == 0 ? 0 : _timingSumMs / _hits, _passes);

    /// <summary>Starts again from the beginning of the bar range.</summary>
    public void Restart(ICollection<NoteAction> output)
    {
        Silence(output);
        Reset();
    }

    /// <summary>Turns off whatever the piano is playing (pause or stop).</summary>
    public void Silence(ICollection<NoteAction> output)
    {
        foreach (var index in _sounding) output.Add(new NoteAction(Song.Notes[index].Pitch, 0, false, Song.Notes[index].Hand));
        _sounding.Clear();
    }

    private void Reset()
    {
        foreach (var index in _practised) _status[index] = NoteStatus.Upcoming;
        for (var i = 0; i < _status.Length; i++)
        {
            if (_status[i] != NoteStatus.OutOfRange && !_practisedSet.Contains(i))
            {
                _status[i] = Setup.PlayOtherHand ? NoteStatus.Accompaniment : NoteStatus.Silent;
            }
        }
        foreach (var chord in _chords)
        {
            chord.Pending.Clear();
            foreach (var index in chord.Notes) chord.Pending.Add(Song.Notes[index].Pitch.MidiNumber);
            chord.Clean = true;
        }

        _nextChord = 0;
        _nextAccompaniment = 0;
        _nextToMiss = 0;
        IsWaiting = false;
        IsFinished = false;
        Position = RangeStart - Setup.LeadInSeconds * _tempo;
    }

    /// <summary>Advances by <paramref name="realSeconds"/> of wall-clock time; notes for the piano go to <paramref name="output"/>.</summary>
    public void Update(double realSeconds, ICollection<NoteAction> output)
    {
        if (IsFinished) return;

        var target = Position + Math.Max(realSeconds, 0) * _tempo;
        if (Mode == PlayAlongMode.Wait && _nextChord < _chords.Count && target >= _chords[_nextChord].Start)
        {
            target = _chords[_nextChord].Start;
            IsWaiting = true;
        }
        Position = target;

        PlayAccompaniment(output);
        if (Mode == PlayAlongMode.Flow) MarkMissed();

        if (Position >= RangeEnd - 1e-9 && !IsWaiting)
        {
            _passes++;
            if (Setup.Loop)
            {
                Restart(output);
                Position = RangeStart - Math.Min(Setup.LeadInSeconds, 1) * _tempo; // shorter run-up on repeats
            }
            else
            {
                Silence(output);
                IsFinished = true;
            }
        }
    }

    private void PlayAccompaniment(ICollection<NoteAction> output)
    {
        for (var i = _sounding.Count - 1; i >= 0; i--)
        {
            var note = Song.Notes[_sounding[i]];
            if (note.End <= Position + 1e-6)
            {
                output.Add(new NoteAction(note.Pitch, 0, false, note.Hand));
                _sounding.RemoveAt(i);
            }
        }

        // While waiting, hold back the accompaniment of the chord being waited for: it sounds with the player's notes.
        var limit = IsWaiting ? _chords[_nextChord].Start - ChordTolerance : Position;
        while (_nextAccompaniment < _accompaniment.Count && Song.Notes[_accompaniment[_nextAccompaniment]].Start <= limit)
        {
            var index = _accompaniment[_nextAccompaniment++];
            var note = Song.Notes[index];
            var already = _sounding.FindIndex(i => Song.Notes[i].Pitch == note.Pitch);
            if (already >= 0)
            {
                output.Add(new NoteAction(note.Pitch, 0, false, note.Hand));
                _sounding.RemoveAt(already);
            }
            output.Add(new NoteAction(note.Pitch, note.Velocity, true, note.Hand));
            _sounding.Add(index);
        }
    }

    private void MarkMissed()
    {
        var late = TimingWindow * _tempo;
        while (_nextToMiss < _practised.Count && Song.Notes[_practised[_nextToMiss]].Start < Position - late)
        {
            var index = _practised[_nextToMiss++];
            if (_status[index] == NoteStatus.Upcoming)
            {
                _status[index] = NoteStatus.Missed;
                _missed++;
            }
        }
    }

    /// <summary>The player pressed a key.</summary>
    public PressResult Press(Pitch pitch) => Mode == PlayAlongMode.Wait ? PressWait(pitch) : PressFlow(pitch);

    private PressResult PressWait(Pitch pitch)
    {
        if (IsFinished || _nextChord >= _chords.Count) return Wrong(null);

        var chord = _chords[_nextChord];
        var inReach = IsWaiting || chord.Start - Position <= EarlyWindow * _tempo;
        if (!inReach || !chord.Pending.Remove(pitch.MidiNumber)) return Wrong(inReach ? chord : null);

        foreach (var index in chord.Notes)
        {
            if (Song.Notes[index].Pitch == pitch) _status[index] = NoteStatus.Hit;
        }

        if (chord.Pending.Count == 0)
        {
            _chordsDone++;
            if (chord.Clean) _cleanChords++;
            _nextChord++;
            IsWaiting = false;
        }
        return PressResult.Correct;
    }

    private PressResult Wrong(Chord? chord)
    {
        _wrongNotes++;
        if (chord is not null) chord.Clean = false;
        return PressResult.Wrong;
    }

    private PressResult PressFlow(Pitch pitch)
    {
        var window = TimingWindow * _tempo;
        int? best = null;
        for (var i = _nextToMiss; i < _practised.Count; i++)
        {
            var index = _practised[i];
            var note = Song.Notes[index];
            if (note.Start > Position + window) break;
            if (note.Pitch != pitch || _status[index] != NoteStatus.Upcoming || Math.Abs(note.Start - Position) > window) continue;
            if (best is null || Math.Abs(note.Start - Position) < Math.Abs(Song.Notes[best.Value].Start - Position)) best = index;
        }

        if (best is not { } hit)
        {
            _wrongNotes++;
            return PressResult.Wrong;
        }

        _status[hit] = NoteStatus.Hit;
        _hits++;
        _timingSumMs += Math.Abs(Position - Song.Notes[hit].Start) / _tempo * 1000;
        return PressResult.Correct;
    }

    /// <summary>Signed timing of a played note relative to the beat, in real ms; for feedback ("early"/"late").</summary>
    public double OffsetMs(SongNote note) => (Position - note.Start) / _tempo * 1000;
}
