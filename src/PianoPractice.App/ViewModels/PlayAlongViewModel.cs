using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using PianoPractice.App.Controls;
using PianoPractice.Core.Exercises;
using PianoPractice.Core.Midi;
using PianoPractice.Core.PlayAlong;
using PianoPractice.Core.Settings;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.ViewModels;

/// <summary>An entry in the song list: a built-in song or a MIDI file.</summary>
public sealed record SongChoice(string Key, string Title, string Detail)
{
    public const string BuiltInPrefix = "builtin:";
    public bool IsFile => !Key.StartsWith(BuiltInPrefix, StringComparison.Ordinal);
    public override string ToString() => Title;
}

/// <summary>A track/channel of a MIDI file and which hand it belongs to.</summary>
public sealed partial class PartChoice(MidiPart part, PartRole role) : ObservableObject
{
    public MidiPart Part { get; } = part;
    public string Label { get; } = part.Describe();
    public IReadOnlyList<string> RoleOptions { get; } = ["Right hand", "Left hand", "Both hands (auto split)", "Split at middle C", "Ignore"];

    [ObservableProperty] public partial int RoleIndex { get; set; } = (int)role;

    public PartRole Role => (PartRole)Math.Clamp(RoleIndex, 0, 4);
}

/// <summary>Play-along: falling notes, wait mode and flow mode for built-in songs and MIDI files.</summary>
public sealed partial class PlayAlongViewModel : ObservableObject, IDisposable
{
    /// <summary>Longest step per frame, so a hitch (window drag, breakpoint) doesn't jump the song.</summary>
    private const double MaxFrameSeconds = 0.1;

    private readonly IMidiService _midi;
    private readonly SettingsStore _settings;
    private readonly ExerciseStatsStore _stats;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly List<NoteAction> _actions = [];
    private readonly Dictionary<int, KeyHighlightKind> _held = [];
    private readonly Dictionary<int, Hand> _sounding = [];

    private Song? _song;
    private MidiSongFile? _midiFile;
    private DateTimeOffset _started;
    private bool _sessionSaved = true;
    private bool _loading;
    private string _lastStatus = "";

    public PlayAlongViewModel(IMidiService midi, SettingsStore settings, [FromKeyedServices(StatsKeys.PlayAlong)] ExerciseStatsStore stats)
    {
        _midi = midi;
        _settings = settings;
        _stats = stats;

        _loading = true;
        var options = settings.Current.PlayAlong;
        HandsIndex = (int)options.Hands;
        ModeIndex = (int)options.Mode;
        PlayOtherHand = options.PlayOtherHand;
        TempoPercent = options.TempoPercent;
        Loop = options.Loop;
        ShowNoteNames = options.ShowNoteNames;
        ViewIndex = options.SheetMusic ? 1 : 0;

        RefreshSongList();
        _loading = false;

        SelectedSong = Songs.FirstOrDefault(s => s.Key == options.LastSong) ?? Songs.FirstOrDefault();
        _midi.NoteReceived += OnNoteReceived;
    }

    #region Song choice

    public ObservableCollection<SongChoice> Songs { get; } = [];
    public ObservableCollection<PartChoice> Parts { get; } = [];

    [ObservableProperty] public partial SongChoice? SelectedSong { get; set; }
    [ObservableProperty] public partial bool HasParts { get; set; }
    [ObservableProperty] public partial string SongInfo { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    public partial string? LoadError { get; set; }

    public bool HasLoadError => LoadError is not null;

    private void RefreshSongList()
    {
        var selected = SelectedSong?.Key;
        Songs.Clear();
        foreach (var id in BuiltInSongs.Ids)
        {
            try
            {
                var song = BuiltInSongs.Load(id);
                Songs.Add(new SongChoice(SongChoice.BuiltInPrefix + id, song.Title, song.Composer ?? ""));
            }
            catch (FormatException)
            {
                // A broken built-in song is a bug caught by the tests; don't take the page down.
            }
        }
        foreach (var path in _settings.Current.PlayAlong.RecentFiles.Where(File.Exists))
        {
            Songs.Add(new SongChoice(path, Path.GetFileNameWithoutExtension(path), path));
        }
        if (selected is not null) SelectedSong = Songs.FirstOrDefault(s => s.Key == selected);
    }

    partial void OnSelectedSongChanged(SongChoice? value)
    {
        if (_loading || value is null) return;
        Stop();
        LoadError = null;
        _midiFile = null;
        Parts.Clear();

        try
        {
            if (value.IsFile)
            {
                _midiFile = MidiSongFile.Load(value.Key);
                var roles = _midiFile.DefaultRoles();
                for (var i = 0; i < _midiFile.Parts.Count; i++)
                {
                    var part = new PartChoice(_midiFile.Parts[i], roles[i]);
                    part.PropertyChanged += OnPartChanged;
                    Parts.Add(part);
                }
                SetSong(_midiFile.ToSong(roles));
            }
            else
            {
                SetSong(BuiltInSongs.Load(value.Key[SongChoice.BuiltInPrefix.Length..]));
            }
            _settings.Update(s => s with { PlayAlong = s.PlayAlong with { LastSong = value.Key } });
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException or Melanchall.DryWetMidi.Common.MidiException)
        {
            LoadError = $"Couldn't open {value.Title}: {ex.Message}";
            SetSong(null);
        }
        HasParts = Parts.Count > 0;
    }

    private void OnPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_midiFile is null || e.PropertyName != nameof(PartChoice.RoleIndex)) return;
        Stop();
        SetSong(_midiFile.ToSong(Parts.Select(p => p.Role).ToList()), keepBars: true);
    }

    /// <summary>Opens a MIDI file chosen by the user and adds it to the recent files.</summary>
    public void OpenFile(string path)
    {
        _settings.Update(s => s with { PlayAlong = s.PlayAlong.WithRecentFile(path) });
        RefreshSongList();
        var choice = Songs.FirstOrDefault(s => s.Key.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (choice is null) return;
        if (SelectedSong == choice) OnSelectedSongChanged(choice); // reload
        else SelectedSong = choice;
    }

    private void SetSong(Song? song, bool keepBars = false)
    {
        _song = song;
        _loading = true;
        BarCount = song?.BarCount ?? 1;
        if (!keepBars || ToBar > BarCount)
        {
            FromBar = 1;
            ToBar = BarCount;
        }
        _loading = false;

        if (song is null)
        {
            SongInfo = "";
            LowestKey = 48;
            HighestKey = 83;
        }
        else
        {
            var (low, high) = song.Range;
            var rh = song.Notes.Count(n => n.Hand == Hand.Right);
            var lh = song.Notes.Count(n => n.Hand == Hand.Left);
            SongInfo = $"{song.BarCount} bars  ·  {song.BeatsPerBar}/{song.BeatUnit}  ·  ♩ = {song.BeatsPerMinute:0}  ·  {song.Key.MajorName} major  ·  RH {rh} notes, LH {lh} notes";
            (LowestKey, HighestKey) = KeyboardRange(low, high);
        }
        RebuildEngine();
    }

    /// <summary>Whole octaves (C to B) around the song, at least three, so keys stay big.</summary>
    private static (int Low, int High) KeyboardRange(Pitch low, Pitch high)
    {
        var lo = low.MidiNumber - low.PitchClass;
        var hi = high.MidiNumber + (11 - high.PitchClass);
        while (hi - lo + 1 < 36)
        {
            if (lo - 12 >= Pitch.PianoLowest && (hi + 12 > Pitch.PianoHighest || 60 - lo < hi - 60)) lo -= 12;
            else if (hi + 12 <= Pitch.PianoHighest) hi += 12;
            else break;
        }
        return (Math.Max(lo, Pitch.PianoLowest), Math.Min(hi, Pitch.PianoHighest));
    }

    #endregion

    #region Options

    public IReadOnlyList<string> HandsOptions { get; } = ["Right hand", "Left hand", "Both hands", "Just listen"];
    public IReadOnlyList<string> ModeOptions { get; } = ["Wait for me", "Keep going (timing)"];

    [ObservableProperty] public partial int HandsIndex { get; set; }
    [ObservableProperty] public partial int ModeIndex { get; set; }
    [ObservableProperty] public partial bool PlayOtherHand { get; set; }
    [ObservableProperty] public partial double TempoPercent { get; set; }
    [ObservableProperty] public partial bool Loop { get; set; }
    [ObservableProperty] public partial bool ShowNoteNames { get; set; }

    public IReadOnlyList<string> ViewOptions { get; } = ["Falling notes", "Sheet music"];

    /// <summary>0 = falling notes, 1 = sheet music.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetMusic), nameof(IsFallingNotes))]
    public partial int ViewIndex { get; set; }

    public bool IsSheetMusic => ViewIndex == 1;
    public bool IsFallingNotes => !IsSheetMusic;

    partial void OnViewIndexChanged(int value) => SaveOptions();
    [ObservableProperty] public partial double FromBar { get; set; } = 1;
    [ObservableProperty] public partial double ToBar { get; set; } = 1;
    [ObservableProperty] public partial int BarCount { get; set; } = 1;
    [ObservableProperty] public partial int LowestKey { get; set; } = 48;
    [ObservableProperty] public partial int HighestKey { get; set; } = 83;

    public string TempoText(double percent) => $"Tempo  {percent:0}%";

    private PracticeHands Hands => (PracticeHands)Math.Clamp(HandsIndex, 0, 3);
    private PlayAlongMode Mode => ModeIndex == 1 ? PlayAlongMode.Flow : PlayAlongMode.Wait;

    partial void OnHandsIndexChanged(int value) => OptionChanged();
    partial void OnModeIndexChanged(int value) => OptionChanged();
    partial void OnPlayOtherHandChanged(bool value) => OptionChanged();
    partial void OnLoopChanged(bool value) => OptionChanged();
    partial void OnFromBarChanged(double value) => OptionChanged();
    partial void OnToBarChanged(double value) => OptionChanged();

    partial void OnShowNoteNamesChanged(bool value) => SaveOptions();

    partial void OnTempoPercentChanged(double value)
    {
        if (double.IsNaN(value)) return;
        if (Engine is not null) Engine.Tempo = value / 100;
        SaveOptions();
    }

    private void OptionChanged()
    {
        if (_loading) return;
        SaveOptions();
        Stop();
        RebuildEngine();
    }

    private void SaveOptions()
    {
        if (_loading) return;
        _settings.Update(s => s with
        {
            PlayAlong = s.PlayAlong with
            {
                Hands = Hands,
                Mode = Mode,
                PlayOtherHand = PlayOtherHand,
                TempoPercent = (int)Math.Clamp(double.IsNaN(TempoPercent) ? 75 : TempoPercent, 10, 150),
                Loop = Loop,
                ShowNoteNames = ShowNoteNames,
                SheetMusic = IsSheetMusic,
            },
        });
    }

    #endregion

    #region Playing

    /// <summary>The current run; replaced whenever the song or an option changes.</summary>
    [ObservableProperty] public partial PlayAlongEngine? Engine { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayButtonText), nameof(PlayButtonSymbol))]
    public partial bool IsPlaying { get; set; }

    public string PlayButtonText => IsPlaying ? "Pause" : "Play";
    public string PlayButtonSymbol => IsPlaying ? "" : "";

    [ObservableProperty] public partial IReadOnlyCollection<KeyHighlight> Highlights { get; set; } = [];
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string ScoreText { get; set; } = "";
    [ObservableProperty] public partial string ResultText { get; set; } = "";
    [ObservableProperty] public partial bool HasResult { get; set; }

    private void RebuildEngine()
    {
        if (_song is null)
        {
            Engine = null;
            StatusText = "";
            return;
        }

        var from = (int)Math.Clamp(double.IsNaN(FromBar) ? 1 : FromBar, 1, BarCount);
        var to = (int)Math.Clamp(double.IsNaN(ToBar) ? BarCount : ToBar, from, BarCount);
        Engine = new PlayAlongEngine(_song, new PlayAlongSetup
        {
            Hands = Hands,
            Mode = Mode,
            PlayOtherHand = PlayOtherHand,
            Tempo = TempoPercent / 100,
            FromBar = from,
            ToBar = to,
            Loop = Loop,
        });
        HasResult = false;
        _held.Clear();
        UpdateDisplay();
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (Engine is null) return;
        if (IsPlaying)
        {
            Pause();
            return;
        }
        if (Engine.IsFinished) Restart();
        if (_sessionSaved)
        {
            _started = DateTimeOffset.Now;
            _sessionSaved = false;
        }
        HasResult = false;
        IsPlaying = true;
    }

    private void Pause()
    {
        IsPlaying = false;
        if (Engine is null) return;
        Engine.Silence(_actions);
        Send();
        UpdateDisplay();
    }

    [RelayCommand]
    private void Restart()
    {
        var wasPlaying = IsPlaying;
        SaveSession();
        if (Engine is null) return;
        Engine.Restart(_actions);
        Send();
        HasResult = false;
        _started = DateTimeOffset.Now;
        _sessionSaved = !wasPlaying;
        UpdateDisplay();
    }

    /// <summary>Stops and silences; the next Play starts a new run.</summary>
    private void Stop()
    {
        if (Engine is not null)
        {
            Engine.Silence(_actions);
            Send();
        }
        SaveSession();
        IsPlaying = false;
    }

    /// <summary>Called every frame by the page.</summary>
    public void Tick(double realSeconds)
    {
        if (!IsPlaying || Engine is null) return;
        Engine.Update(Math.Min(realSeconds, MaxFrameSeconds), _actions);
        Send();

        if (Engine.IsFinished)
        {
            IsPlaying = false;
            ShowResult();
            SaveSession();
        }
        UpdateDisplay();
    }

    private void Send()
    {
        foreach (var action in _actions)
        {
            if (action.IsOn)
            {
                _midi.NoteOn(action.Pitch, action.Velocity);
                _sounding[action.Pitch.MidiNumber] = action.Hand;
            }
            else
            {
                _midi.NoteOff(action.Pitch);
                _sounding.Remove(action.Pitch.MidiNumber);
            }
        }
        _actions.Clear();
    }

    private void OnNoteReceived(object? sender, MidiNoteEvent e) =>
        _dispatcher.TryEnqueue(() =>
        {
            if (e.IsNoteOn) Played(e.Pitch);
            else Released(e.Pitch);
        });

    public void ScreenKeyDown(int midi)
    {
        _midi.NoteOn(new Pitch(midi));
        Played(new Pitch(midi));
    }

    public void ScreenKeyUp(int midi)
    {
        _midi.NoteOff(new Pitch(midi));
        Released(new Pitch(midi));
    }

    private void Played(Pitch pitch)
    {
        if (IsPlaying && Engine is not null && Engine.Setup.Hands != PracticeHands.None)
        {
            _held[pitch.MidiNumber] = Engine.Press(pitch) == PressResult.Correct ? KeyHighlightKind.Correct : KeyHighlightKind.Wrong;
        }
        else
        {
            _held[pitch.MidiNumber] = KeyHighlightKind.Played;
        }
        UpdateDisplay();
    }

    private void Released(Pitch pitch)
    {
        if (_held.Remove(pitch.MidiNumber)) UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var engine = Engine;
        var highlights = new Dictionary<int, KeyHighlight>();
        if (engine is not null)
        {
            // Keys the piano is playing, faintly; keys to play next, a bit stronger; your own keys on top.
            foreach (var (midi, hand) in _sounding) highlights[midi] = new KeyHighlight(midi, HandKind(hand), 0f);
            var lookAhead = engine.NextChordStart is { } next && (engine.IsWaiting || next - engine.Position < 1.0 * engine.Tempo);
            if (lookAhead)
            {
                foreach (var note in engine.ExpectedNotes)
                {
                    highlights[note.Pitch.MidiNumber] = new KeyHighlight(note.Pitch.MidiNumber, HandKind(note.Hand), engine.IsWaiting ? 0.6f : 0.2f);
                }
            }
        }
        foreach (var (midi, kind) in _held) highlights[midi] = new KeyHighlight(midi, kind);

        if (!highlights.Values.ToHashSet().SetEquals(Highlights)) Highlights = highlights.Values.ToList();

        var status = Status(engine);
        if (status != _lastStatus) StatusText = _lastStatus = status;
        ScoreText = Score(engine);
    }

    private static KeyHighlightKind HandKind(Hand hand) => hand == Hand.Right ? KeyHighlightKind.RightHand : KeyHighlightKind.LeftHand;

    private string Status(PlayAlongEngine? engine)
    {
        if (engine is null) return _song is null ? "" : "No notes to play.";
        var bar = $"Bar {engine.CurrentBar} of {engine.FromBar}–{engine.ToBar}";
        if (engine.IsFinished) return $"{bar}  ·  Finished";
        if (!IsPlaying) return engine.Position <= engine.RangeStart ? "Press Play (or Space) to start." : $"{bar}  ·  Paused";
        if (engine.Setup.Hands == PracticeHands.None) return $"{bar}  ·  Listening";
        if (engine.IsWaiting)
        {
            var names = engine.ExpectedNotes.OrderBy(n => n.Pitch).Select(n => NoteNames.WithOctave(n.Pitch)).Distinct();
            return $"{bar}  ·  Play  {string.Join("  ", names)}";
        }
        return bar;
    }

    private static string Score(PlayAlongEngine? engine)
    {
        if (engine is null || engine.Setup.Hands == PracticeHands.None) return "";
        var r = engine.Result;
        return engine.Mode == PlayAlongMode.Wait
            ? (r.Chords == 0 && r.WrongNotes == 0 ? "" : $"{r.CleanChords} / {r.Chords} clean  ·  {r.WrongNotes} wrong")
            : (r.NotesHit + r.NotesMissed + r.WrongNotes == 0 ? "" : $"{r.NotesHit} hit  ·  {r.NotesMissed} missed  ·  {r.WrongNotes} wrong  ·  ±{r.MeanAbsTimingMs:0} ms");
    }

    private void ShowResult()
    {
        if (Engine is null) return;
        var r = Engine.Result;
        if (Engine.Setup.Hands == PracticeHands.None)
        {
            ResultText = "That's how it sounds. Now try it yourself!";
        }
        else
        {
            var accuracy = r.Accuracy(Engine.Mode);
            var headline = accuracy switch { >= 0.95 => "Excellent!", >= 0.8 => "Well done!", >= 0.6 => "Getting there.", _ => "Keep practising." };
            var detail = Engine.Mode == PlayAlongMode.Wait
                ? $"{r.CleanChords} of {r.Chords} chords without a wrong note  ·  {r.WrongNotes} wrong notes"
                : $"{r.NotesHit} of {r.NotesHit + r.NotesMissed} notes in time  ·  {r.WrongNotes} wrong  ·  ±{r.MeanAbsTimingMs:0} ms";
            ResultText = $"{headline}  {accuracy:P0}\n{detail}";
        }
        HasResult = true;
    }

    /// <summary>Records the run in the stats, once, if something was played.</summary>
    private void SaveSession()
    {
        if (_sessionSaved || Engine is null || _song is null) return;
        _sessionSaved = true;
        var e = Engine;
        if (e.Setup.Hands == PracticeHands.None) return;

        var r = e.Result;
        var (cards, correct) = e.Mode == PlayAlongMode.Wait ? (r.Chords, r.CleanChords) : (r.NotesHit + r.NotesMissed, r.NotesHit);
        if (cards == 0) return;

        var hands = e.Setup.Hands switch { PracticeHands.Right => "RH", PracticeHands.Left => "LH", _ => "both hands" };
        var options = $"{_song.Title}  ·  {hands}  ·  {(e.Mode == PlayAlongMode.Wait ? "wait" : "flow")}  ·  {e.Tempo * 100:0}%  ·  bars {e.FromBar}–{e.ToBar}";
        _stats.AddSession(new SessionSummary(_started, options, cards, correct, r.MeanAbsTimingMs));
    }

    #endregion

    public void Dispose()
    {
        Stop();
        _midi.NoteReceived -= OnNoteReceived;
        foreach (var part in Parts) part.PropertyChanged -= OnPartChanged;
    }
}
