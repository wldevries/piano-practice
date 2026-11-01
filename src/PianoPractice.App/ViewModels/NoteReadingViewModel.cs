using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PianoPractice.App.Controls;
using PianoPractice.Core.Exercises.NoteReading;
using PianoPractice.Core.Midi;
using PianoPractice.Core.Settings;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.ViewModels;

public enum NoteReadingPhase { Setup, Running, Summary }

/// <summary>Flashcards: read the note on the staff, play it on the piano.</summary>
public sealed partial class NoteReadingViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CorrectPause = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan RetryPause = TimeSpan.FromMilliseconds(450);

    private readonly IMidiService _midi;
    private readonly SettingsStore _settings;
    private readonly NoteReadingStatsStore _stats;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Random _random = new();

    private NoteReadingSession? _session;
    private NoteReadingOptions _options;
    private int _advanceToken; // invalidates pending auto-advances

    public NoteReadingViewModel(IMidiService midi, SettingsStore settings, NoteReadingStatsStore stats)
    {
        _midi = midi;
        _settings = settings;
        _stats = stats;
        _options = settings.Current.NoteReading;

        SelectedClefIndex = (int)_options.Clefs;
        SelectedLedgerIndex = Math.Clamp(_options.LedgerLines, 0, LedgerOptions.Count - 1);
        IncludeAccidentals = _options.IncludeAccidentals;
        RequireExactOctave = _options.RequireExactOctave;
        SelectedKey = new KeySignature(_options.KeyFifths);
        CardsPerSession = _options.CardsPerSession;
        UpdateDeckSize();
        RefreshRecentSessions();

        _midi.NoteReceived += OnNoteReceived;
    }

    #region Phase

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup), nameof(IsRunning), nameof(IsSummary), nameof(IsKeyboardVisible))]
    public partial NoteReadingPhase Phase { get; set; } = NoteReadingPhase.Setup;

    public bool IsSetup => Phase == NoteReadingPhase.Setup;
    public bool IsRunning => Phase == NoteReadingPhase.Running;
    public bool IsSummary => Phase == NoteReadingPhase.Summary;
    public bool IsKeyboardVisible => Phase != NoteReadingPhase.Setup;

    #endregion

    #region Setup

    public IReadOnlyList<string> ClefOptions { get; } = ["Treble clef", "Bass clef", "Both clefs"];
    public IReadOnlyList<string> LedgerOptions { get; } = ["On the staff only", "Up to 1 ledger line", "Up to 2 ledger lines", "Up to 3 ledger lines"];
    public IReadOnlyList<KeySignature> Keys => KeySignature.All;

    [ObservableProperty] public partial int SelectedClefIndex { get; set; }
    [ObservableProperty] public partial int SelectedLedgerIndex { get; set; }
    [ObservableProperty] public partial bool IncludeAccidentals { get; set; }
    [ObservableProperty] public partial bool RequireExactOctave { get; set; }
    [ObservableProperty] public partial KeySignature SelectedKey { get; set; }
    [ObservableProperty] public partial double CardsPerSession { get; set; }
    [ObservableProperty] public partial string DeckSizeText { get; set; } = "";

    public ObservableCollection<string> RecentSessions { get; } = [];

    partial void OnSelectedClefIndexChanged(int value) => UpdateDeckSize();
    partial void OnSelectedLedgerIndexChanged(int value) => UpdateDeckSize();
    partial void OnIncludeAccidentalsChanged(bool value) => UpdateDeckSize();
    partial void OnSelectedKeyChanged(KeySignature value) => UpdateDeckSize();

    private NoteReadingOptions OptionsFromSetup() => new()
    {
        Clefs = (ClefChoice)Math.Clamp(SelectedClefIndex, 0, 2),
        LedgerLines = Math.Max(SelectedLedgerIndex, 0),
        IncludeAccidentals = IncludeAccidentals,
        RequireExactOctave = RequireExactOctave,
        KeyFifths = SelectedKey.Fifths,
        CardsPerSession = double.IsNaN(CardsPerSession) ? 20 : (int)Math.Clamp(CardsPerSession, 5, 200),
    };

    private void UpdateDeckSize()
    {
        var deck = NoteReadingDeck.Build(OptionsFromSetup());
        var known = deck.Count(c => _stats.Data.For(c) is { Attempts: > 0 });
        DeckSizeText = $"{deck.Count} different notes · {known} practised before";
    }

    private void RefreshRecentSessions()
    {
        RecentSessions.Clear();
        foreach (var s in _stats.Data.Sessions.AsEnumerable().Reverse().Take(8))
        {
            RecentSessions.Add($"{s.Started.ToLocalTime():ddd d MMM HH:mm}  ·  {s.Accuracy:P0}  ·  {s.AverageMs / 1000:F1} s  ·  {s.Options}");
        }
        if (RecentSessions.Count == 0) RecentSessions.Add("No sessions yet.");
    }

    [RelayCommand]
    private void Start()
    {
        _options = OptionsFromSetup();
        _settings.Update(s => s with { NoteReading = _options });

        _session = new NoteReadingSession(_options, _stats, _random, TimeProvider.System);
        StaffKey = new KeySignature(_options.KeyFifths);
        Phase = NoteReadingPhase.Running;
        ShowCurrentCard();
    }

    #endregion

    #region Running

    [ObservableProperty] public partial IReadOnlyList<StaffNote> StaffNotes { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyCollection<KeyHighlight> Highlights { get; set; } = [];
    [ObservableProperty] public partial KeySignature StaffKey { get; set; }
    [ObservableProperty] public partial string ProgressText { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string FeedbackText { get; set; } = "";
    [ObservableProperty] public partial string ScoreText { get; set; } = "";

    private void ShowCurrentCard()
    {
        if (_session is null) return;
        var card = _session.Current;
        StaffNotes = [new StaffNote(card.Pitch, null, card.Clef)];
        Highlights = [];
        FeedbackText = "Play the note";
        ProgressText = $"{_session.Number} / {_session.Total}";
        Progress = (_session.Number - 1) * 100.0 / _session.Total;
        ScoreText = _session.AnsweredCount == 0 ? "" : $"{_session.CorrectCount} / {_session.AnsweredCount} correct";
    }

    private void OnNoteReceived(object? sender, MidiNoteEvent e)
    {
        if (e.IsNoteOn) _dispatcher.TryEnqueue(() => Answer(e.Pitch));
    }

    /// <summary>On-screen key: sound it on the piano and treat it as an answer.</summary>
    public void ScreenKeyDown(int midi)
    {
        _midi.NoteOn(new Pitch(midi));
        if (IsRunning) Answer(new Pitch(midi));
    }

    public void ScreenKeyUp(int midi) => _midi.NoteOff(new Pitch(midi));

    private void Answer(Pitch played)
    {
        if (_session is null || !IsRunning) return;
        var card = _session.Current;
        var target = card.Pitch.Pitch;
        var playedName = StaffKey.Spell(played).ToString();

        switch (_session.Answer(played))
        {
            case AnswerOutcome.Correct:
                StaffNotes = [new StaffNote(card.Pitch, KeyHighlightKind.Correct, card.Clef)];
                Highlights = [new KeyHighlight(played.MidiNumber, KeyHighlightKind.Correct)];
                FeedbackText = $"✓  {card.Pitch}  ·  {_session.LastAnswerTime.TotalSeconds:F1} s";
                ScheduleAdvance(CorrectPause);
                break;

            case AnswerOutcome.Wrong:
            case AnswerOutcome.RetryWrong:
                StaffNotes = [new StaffNote(card.Pitch, KeyHighlightKind.Wrong, card.Clef)];
                Highlights =
                [
                    new KeyHighlight(played.MidiNumber, KeyHighlightKind.Wrong),
                    new KeyHighlight(target.MidiNumber, KeyHighlightKind.Hint),
                ];
                FeedbackText = $"✗  That was {playedName}. This is {card.Pitch}: play it to continue.";
                break;

            case AnswerOutcome.RetryCorrect:
                StaffNotes = [new StaffNote(card.Pitch, KeyHighlightKind.Correct, card.Clef)];
                Highlights = [new KeyHighlight(played.MidiNumber, KeyHighlightKind.Correct)];
                FeedbackText = $"✓  {card.Pitch}";
                ScheduleAdvance(RetryPause);
                break;
        }
        ScoreText = $"{_session.CorrectCount} / {_session.AnsweredCount} correct";
    }

    private async void ScheduleAdvance(TimeSpan delay)
    {
        var token = ++_advanceToken;
        await Task.Delay(delay);
        if (token == _advanceToken) Next();
    }

    private void Next()
    {
        if (_session is null) return;
        _session.Advance();
        if (_session.IsFinished) ShowSummary();
        else ShowCurrentCard();
    }

    [RelayCommand]
    private void Skip()
    {
        _advanceToken++;
        Next();
    }

    [RelayCommand]
    private void Stop()
    {
        _advanceToken++;
        if (_session is null) return;
        _session.Finish();
        if (_session.AnsweredCount > 0) ShowSummary();
        else BackToSetup();
    }

    #endregion

    #region Summary

    [ObservableProperty] public partial string SummaryAccuracy { get; set; } = "";
    [ObservableProperty] public partial string SummaryTime { get; set; } = "";
    [ObservableProperty] public partial string SummaryDetail { get; set; } = "";
    public ObservableCollection<string> TroubleNotes { get; } = [];

    private void ShowSummary()
    {
        if (_session is null) return;
        var summary = _session.Summary;
        SummaryAccuracy = $"{summary.Accuracy:P0}";
        SummaryTime = $"{summary.AverageMs / 1000:F1} s";
        SummaryDetail = $"{summary.Correct} of {summary.Cards} right first time  ·  {summary.Options}";

        // All-time accuracy per key over the cards in this note set: green = solid, red = needs work.
        var perKey = _session.Deck
            .Select(c => (Midi: c.Pitch.Pitch.MidiNumber, Stats: _stats.Data.For(c)))
            .Where(x => x.Stats is { Attempts: > 0 })
            .GroupBy(x => x.Midi)
            .Select(g => (Midi: g.Key, Accuracy: g.Sum(x => x.Stats!.Correct) / (double)g.Sum(x => x.Stats!.Attempts)));
        Highlights = perKey
            .Select(k => k.Accuracy >= 0.8
                ? new KeyHighlight(k.Midi, KeyHighlightKind.Correct, (float)((k.Accuracy - 0.8) / 0.2))
                : new KeyHighlight(k.Midi, KeyHighlightKind.Wrong, (float)(1 - k.Accuracy / 0.8)))
            .ToList();

        TroubleNotes.Clear();
        var reference = CardPicker.ReferenceMs(_stats.Data);
        var trouble = _session.Deck
            .Select(c => (Card: c, Stats: _stats.Data.For(c)))
            .Where(x => x.Stats is { Attempts: > 0 })
            .Select(x => (x.Card, x.Stats, Weight: CardPicker.Weight(x.Stats, reference)))
            .OrderByDescending(x => x.Weight)
            .Take(5)
            .Where(x => x.Weight > 2.5)
            .Select(x => (x.Card, x.Stats));
        foreach (var (card, stats) in trouble)
        {
            TroubleNotes.Add($"{card}  ·  {stats!.Correct}/{stats.Attempts} right  ·  {stats.AverageMs / 1000:F1} s");
        }
        if (TroubleNotes.Count == 0) TroubleNotes.Add("Nothing stands out. Try a wider range or more ledger lines.");

        StaffNotes = [];
        Phase = NoteReadingPhase.Summary;
    }

    [RelayCommand]
    private void Again() => Start();

    [RelayCommand]
    private void BackToSetup()
    {
        _session = null;
        Highlights = [];
        StaffNotes = [];
        UpdateDeckSize();
        RefreshRecentSessions();
        Phase = NoteReadingPhase.Setup;
    }

    #endregion

    public void Dispose()
    {
        _advanceToken++;
        _midi.NoteReceived -= OnNoteReceived;
        if (_session is { IsFinished: false }) _session.Finish(); // leaving mid-session still saves the stats
    }
}
