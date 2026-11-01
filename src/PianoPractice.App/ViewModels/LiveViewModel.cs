using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using PianoPractice.App.Controls;
using PianoPractice.Core.Midi;
using PianoPractice.Core.Settings;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.ViewModels;

/// <summary>Shows whatever is being played: keys, note names, chord symbol, sustain pedal.</summary>
public sealed partial class LiveViewModel : ObservableObject, IDisposable
{
    private const int OnScreenVelocity = 80;

    private readonly IMidiService _midi;
    private readonly SettingsStore _settings;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private readonly Dictionary<int, int> _pianoHeld = new(); // midi → velocity
    private readonly HashSet<int> _screenHeld = new();

    public LiveViewModel(IMidiService midi, SettingsStore settings)
    {
        _midi = midi;
        _settings = settings;
        SelectedKey = new KeySignature(settings.Current.LiveKeyFifths);
        _midi.NoteReceived += OnNoteReceived;
        _midi.SustainChanged += OnSustainChanged;
        _midi.ConnectionChanged += OnConnectionChanged;

#if DEBUG
        // Dev aid for screenshots without a piano: PIANOPRACTICE_DEMO_NOTES=60,64,67 shows those notes as held.
        if (Environment.GetEnvironmentVariable("PIANOPRACTICE_DEMO_NOTES") is { Length: > 0 } demo)
        {
            foreach (var part in demo.Split(','))
            {
                if (int.TryParse(part, out var note)) _pianoHeld[note] = 100;
            }
            Refresh();
        }
#endif
    }

    /// <summary>Unplugged mid-note: the note-offs will never arrive, so drop what was held.</summary>
    private void OnConnectionChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        if (_midi.IsInputConnected || _pianoHeld.Count == 0 && !IsSustainOn) return;
        _pianoHeld.Clear();
        IsSustainOn = false;
        Refresh();
    });

    [ObservableProperty]
    public partial IReadOnlyCollection<KeyHighlight> Highlights { get; set; } = [];

    [ObservableProperty]
    public partial string ChordName { get; set; } = "";

    [ObservableProperty]
    public partial string NoteNamesText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSustainOn { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<StaffNote> StaffNotes { get; set; } = [];

    public IReadOnlyList<KeySignature> Keys => KeySignature.All;

    /// <summary>Key used to spell held notes and draw the key signature.</summary>
    [ObservableProperty]
    public partial KeySignature SelectedKey { get; set; }

    partial void OnSelectedKeyChanged(KeySignature value)
    {
        if (_settings.Current.LiveKeyFifths != value.Fifths)
        {
            _settings.Update(s => s with { LiveKeyFifths = value.Fifths });
        }
        Refresh();
    }

    private void OnNoteReceived(object? sender, MidiNoteEvent e) => _dispatcher.TryEnqueue(() =>
    {
        if (e.IsNoteOn) _pianoHeld[e.Pitch.MidiNumber] = e.Velocity;
        else _pianoHeld.Remove(e.Pitch.MidiNumber);
        Refresh();
    });

    private void OnSustainChanged(object? sender, bool isOn) => _dispatcher.TryEnqueue(() => IsSustainOn = isOn);

    /// <summary>On-screen key pressed: sound it on the piano and treat it as held.</summary>
    public void ScreenKeyDown(int midi)
    {
        if (!_screenHeld.Add(midi)) return;
        _midi.NoteOn(new Pitch(midi), OnScreenVelocity);
        Refresh();
    }

    public void ScreenKeyUp(int midi)
    {
        if (!_screenHeld.Remove(midi)) return;
        _midi.NoteOff(new Pitch(midi));
        Refresh();
    }

    private void Refresh()
    {
        var highlights = new List<KeyHighlight>();
        foreach (var (midi, velocity) in _pianoHeld)
        {
            highlights.Add(new KeyHighlight(midi, KeyHighlightKind.Played, velocity / 127f));
        }
        foreach (var midi in _screenHeld.Where(m => !_pianoHeld.ContainsKey(m)))
        {
            highlights.Add(new KeyHighlight(midi, KeyHighlightKind.Sounded));
        }
        Highlights = highlights;

        var held = _pianoHeld.Keys.Union(_screenHeld).Order().Select(m => new Pitch(m)).ToList();
        var spelled = held.Select(p => SelectedKey.Spell(p)).ToList();
        StaffNotes = held
            .Select((p, i) => new StaffNote(spelled[i], _pianoHeld.ContainsKey(p.MidiNumber) ? null : KeyHighlightKind.Sounded))
            .ToList();
        NoteNamesText = string.Join("  ", spelled);
        ChordName = ChordDetector.Detect(held)?.Name
            ?? (held.Count == 2 ? NoteNames.Interval(held[1].MidiNumber - held[0].MidiNumber) : "");
    }

    public void Dispose()
    {
        _midi.NoteReceived -= OnNoteReceived;
        _midi.SustainChanged -= OnSustainChanged;
        _midi.ConnectionChanged -= OnConnectionChanged;
        foreach (var midi in _screenHeld) _midi.NoteOff(new Pitch(midi));
        _screenHeld.Clear();
    }
}
