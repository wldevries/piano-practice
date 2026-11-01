using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.PlayAlong;

/// <summary>What a part of a MIDI file is used for.</summary>
public enum PartRole
{
    RightHand,
    LeftHand,
    /// <summary>Both hands, split by following where each hand is (see <see cref="HandSplitter"/>).</summary>
    SplitAuto,
    /// <summary>Notes from middle C up go to the right hand, the rest to the left.</summary>
    SplitByPitch,
    Ignore,
}

/// <summary>The notes of one track and channel in a MIDI file.</summary>
/// <param name="Program">General MIDI instrument (0-based), if the file sets one.</param>
public sealed record MidiPart(int Track, int Channel, string Name, IReadOnlyList<SongNote> Notes, int? Program = null)
{
    private static readonly string[] MelodyWords = ["melody", "vocal", "voice", "vox", "lead", "sing", "tune", "theme"];

    /// <summary>Channel 10, or a percussion instrument / name: never something to play on the keys.</summary>
    public bool IsDrums =>
        Channel == 9
        || Program is 113 or 115 or 116 or 117 or 118 or 119
        || Name.Contains("drum", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("perc", StringComparison.OrdinalIgnoreCase);

    public bool LooksLikeMelody => MelodyWords.Any(w => Name.Contains(w, StringComparison.OrdinalIgnoreCase));
    public bool LooksLikeBass => Name.Contains("bass", StringComparison.OrdinalIgnoreCase) || Program is >= 32 and <= 39;
    public bool LooksLikePiano => Name.Contains("piano", StringComparison.OrdinalIgnoreCase) || Program is >= 0 and <= 7;

    public double AveragePitch => Notes.Count == 0 ? 0 : Notes.Average(n => n.Pitch.MidiNumber);

    public string Describe()
    {
        var instrument = Program is { } p ? GeneralMidi.InstrumentName(p) : null;
        var name = Name.Length > 0 ? Name : instrument ?? $"Track {Track + 1}";
        var showInstrument = instrument is not null && !name.Equals(instrument, StringComparison.OrdinalIgnoreCase);
        return name +
            (showInstrument ? $"  ({instrument})" : "") +
            $"  ·  {Notes.Count} notes" +
            (Notes.Count > 0 ? $"  ·  {Notes.Min(n => n.Pitch)}–{Notes.Max(n => n.Pitch)}" : "") +
            (IsDrums ? "  ·  drums" : "");
    }
}

/// <summary>A parsed MIDI file: its parts, bars and tempo, before the parts are assigned to hands.</summary>
public sealed class MidiSongFile
{
    public const int SplitPoint = 60; // middle C goes to the right hand

    private MidiSongFile(string title, IReadOnlyList<MidiPart> parts, IReadOnlyList<double> barStarts,
        double beatsPerMinute, int beatsPerBar, int beatUnit, KeySignature key)
    {
        Title = title;
        Parts = parts;
        BarStarts = barStarts;
        BeatsPerMinute = beatsPerMinute;
        BeatsPerBar = beatsPerBar;
        BeatUnit = beatUnit;
        Key = key;
    }

    public string Title { get; }
    public IReadOnlyList<MidiPart> Parts { get; }
    public IReadOnlyList<double> BarStarts { get; }
    public double BeatsPerMinute { get; }
    public int BeatsPerBar { get; }
    public int BeatUnit { get; }
    public KeySignature Key { get; }

    public static MidiSongFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, Path.GetFileNameWithoutExtension(path));
    }

    public static MidiSongFile Read(Stream stream, string title)
    {
        var file = MidiFile.Read(stream, new ReadingSettings
        {
            // Plenty of files on the internet are slightly broken; be forgiving.
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
            InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.ReadValid,
            InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
            MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
            UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
            UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
        });
        var tempoMap = file.GetTempoMap();

        double Seconds(long ticks) => TimeConverter.ConvertTo<MetricTimeSpan>(ticks, tempoMap).TotalMicroseconds / 1_000_000.0;

        var parts = new List<MidiPart>();
        var trackIndex = 0;
        foreach (var track in file.GetTrackChunks())
        {
            var name = track.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text?.Trim() ?? "";
            foreach (var channelGroup in track.GetNotes().GroupBy(n => (int)n.Channel).OrderBy(g => g.Key))
            {
                var notes = channelGroup
                    .Where(n => n.NoteNumber >= Pitch.PianoLowest && n.NoteNumber <= Pitch.PianoHighest)
                    .Select(n =>
                    {
                        var start = Seconds(n.Time);
                        return new SongNote(new Pitch(n.NoteNumber), start, Math.Max(Seconds(n.EndTime) - start, 0.05), Hand.Right, n.Velocity);
                    })
                    .ToList();
                var program = track.Events.OfType<ProgramChangeEvent>().FirstOrDefault(e => e.Channel == channelGroup.Key)?.ProgramNumber;
                if (notes.Count > 0) parts.Add(new MidiPart(trackIndex, channelGroup.Key, name, notes, program is { } p ? (int)p : null));
            }
            trackIndex++;
        }

        var lastTick = file.GetTimedEvents().Select(e => e.Time).DefaultIfEmpty(0).Max();
        var barStarts = new List<double>();
        for (var bar = 0; ; bar++)
        {
            var ticks = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar, 0, 0), tempoMap);
            if (ticks >= lastTick && bar > 0) break;
            barStarts.Add(Seconds(ticks));
            if (bar > 10_000) break;
        }

        var tempo = tempoMap.GetTempoAtTime(new MidiTimeSpan(0));
        var timeSignature = tempoMap.GetTimeSignatureAtTime(new MidiTimeSpan(0));
        var keyEvent = file.GetTimedEvents().Select(e => e.Event).OfType<KeySignatureEvent>().FirstOrDefault();
        // Many sequencers write C major by default, so a C major event says little: guess from the notes then.
        var key = keyEvent is null || keyEvent.Key == 0
            ? KeyGuess.FromNotes(parts.Where(p => !p.IsDrums).SelectMany(p => p.Notes).Select(n => (n.Pitch, n.Duration)))
            : new KeySignature(Math.Clamp((int)keyEvent.Key, -7, 7));

        // The file name is usually a better title than the sequence name ("Untitled", "Track 1", ...).
        return new MidiSongFile(title, parts, barStarts, tempo.BeatsPerMinute, timeSignature.Numerator, timeSignature.Denominator, key);
    }

    public IReadOnlyList<PartRole> DefaultRoles() => DefaultRolesFor(Parts);

    /// <summary>
    /// A playable guess. Drums are always ignored. One part (a piano solo) is split at middle C; two parts
    /// are right and left hand, the higher one on the right. A band arrangement gets the melody in the right
    /// hand and the bass in the left, and everything else is ignored: the whole band would be unplayable.
    /// </summary>
    public static IReadOnlyList<PartRole> DefaultRolesFor(IReadOnlyList<MidiPart> parts)
    {
        var roles = Enumerable.Repeat(PartRole.Ignore, parts.Count).ToArray();
        var melodic = Enumerable.Range(0, parts.Count).Where(i => !parts[i].IsDrums).ToList();

        void HighLow(int a, int b)
        {
            var aHigher = parts[a].AveragePitch >= parts[b].AveragePitch;
            roles[aHigher ? a : b] = PartRole.RightHand;
            roles[aHigher ? b : a] = PartRole.LeftHand;
        }

        switch (melodic.Count)
        {
            case 0:
                return roles;
            case 1:
                roles[melodic[0]] = PartRole.SplitAuto;
                return roles;
            case 2:
                HighLow(melodic[0], melodic[1]);
                return roles;
        }

        var melody = melodic.Where(i => parts[i].LooksLikeMelody).Cast<int?>().FirstOrDefault();
        var bass = melodic.Where(i => parts[i].LooksLikeBass && i != melody).OrderByDescending(i => parts[i].Name.Contains("bass", StringComparison.OrdinalIgnoreCase)).Cast<int?>().FirstOrDefault();
        if (melody is null)
        {
            // A piano arrangement among other instruments: play the piano part(s).
            var pianos = melodic.Where(i => parts[i].LooksLikePiano).ToList();
            if (pianos.Count == 1) { roles[pianos[0]] = PartRole.SplitAuto; return roles; }
            if (pianos.Count == 2) { HighLow(pianos[0], pianos[1]); return roles; }

            // Otherwise the busiest part above the bass register is the best guess for a tune.
            melody = melodic.Where(i => i != bass && parts[i].AveragePitch >= 55).OrderByDescending(i => parts[i].Notes.Count).Cast<int?>().FirstOrDefault();
        }

        if (melody is { } m) roles[m] = PartRole.RightHand;
        if (bass is { } b) roles[b] = PartRole.LeftHand;
        if (melody is null && bass is null) roles[melodic[0]] = PartRole.SplitAuto;
        return roles;
    }

    public Song ToSong(IReadOnlyList<PartRole> roles)
    {
        if (roles.Count != Parts.Count) throw new ArgumentException("One role per part is needed.", nameof(roles));

        // Auto-split parts are split together, so two halves of one piano (separate channels) don't collide.
        var notes = HandSplitter.Split(Enumerable.Range(0, Parts.Count).Where(i => roles[i] == PartRole.SplitAuto).SelectMany(i => Parts[i].Notes));
        for (var i = 0; i < Parts.Count; i++)
        {
            foreach (var note in Parts[i].Notes)
            {
                Hand? hand = roles[i] switch
                {
                    PartRole.RightHand => Hand.Right,
                    PartRole.LeftHand => Hand.Left,
                    PartRole.SplitByPitch => note.Pitch.MidiNumber >= SplitPoint ? Hand.Right : Hand.Left,
                    _ => null,
                };
                if (hand is { } h) notes.Add(note with { Hand = h });
            }
        }

        // The same key struck twice at once (doubled parts) would need two presses; keep one.
        notes = notes.GroupBy(n => (n.Pitch, Math.Round(n.Start, 3))).Select(g => g.MaxBy(n => n.Duration)!).ToList();

        return new Song(Title, notes, BarStarts, BeatsPerMinute, BeatsPerBar, BeatUnit, Key);
    }

    /// <summary>Writes a song as a two-track MIDI file (right hand, left hand) at a constant tempo.</summary>
    public static void Write(Song song, Stream stream)
    {
        const short TicksPerQuarter = 480;
        var ticksPerSecond = song.BeatsPerMinute / 60.0 * TicksPerQuarter;
        long Ticks(double seconds) => (long)Math.Round(seconds * ticksPerSecond);

        var conductor = new TrackChunk(
            new SequenceTrackNameEvent(song.Title),
            new SetTempoEvent((long)Math.Round(60_000_000 / song.BeatsPerMinute)),
            new TimeSignatureEvent((byte)song.BeatsPerBar, (byte)song.BeatUnit),
            new KeySignatureEvent((sbyte)song.Key.Fifths, 0));

        var tracks = new List<TrackChunk> { conductor };
        foreach (var (hand, name, channel) in new[] { (Hand.Right, "Right hand", 0), (Hand.Left, "Left hand", 1) })
        {
            var events = song.Notes.Where(n => n.Hand == hand)
                .SelectMany(n => new (long Time, int Order, MidiEvent Event)[]
                {
                    (Ticks(n.Start), 1, new NoteOnEvent((SevenBitNumber)n.Pitch.MidiNumber, (SevenBitNumber)n.Velocity) { Channel = (FourBitNumber)channel }),
                    (Ticks(n.End), 0, new NoteOffEvent((SevenBitNumber)n.Pitch.MidiNumber, (SevenBitNumber)0) { Channel = (FourBitNumber)channel }),
                })
                .OrderBy(e => e.Time).ThenBy(e => e.Order) // note-offs first, so repeated notes retrigger
                .ToList();

            var track = new TrackChunk(new SequenceTrackNameEvent(name));
            var previous = 0L;
            foreach (var (time, _, midiEvent) in events)
            {
                midiEvent.DeltaTime = time - previous;
                previous = time;
                track.Events.Add(midiEvent);
            }
            tracks.Add(track);
        }

        var file = new MidiFile(tracks) { TimeDivision = new TicksPerQuarterNoteTimeDivision(TicksPerQuarter) };
        file.Write(stream, MidiFileFormat.MultiTrack);
    }
}
