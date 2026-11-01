using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using PianoPractice.App.Controls;
using PianoPractice.Core.Exercises;
using PianoPractice.Core.Exercises.Intervals;
using PianoPractice.Core.Midi;
using PianoPractice.Core.Settings;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.ViewModels;

public enum IntervalsPhase { Setup, Running, Summary }

/// <summary>A checkbox in the interval set.</summary>
public sealed partial class IntervalChoice(IntervalInfo info) : ObservableObject
{
    public IntervalInfo Info { get; } = info;
    public int Semitones => Info.Semitones;
    public string Label => $"{Info.Short}  ·  {Info.Name}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Ear training: the piano plays an interval, the player plays it back or names it.</summary>
public sealed partial class IntervalsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CorrectPause = TimeSpan.FromMilliseconds(1200);

    private readonly IMidiService _midi;
    private readonly SettingsStore _settings;
    private readonly ExerciseStatsStore _stats;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Random _random = new();

    private IntervalSession? _session;
    private CancellationTokenSource? _playback;
    private int _advanceToken;
    private bool _applyingStage;

    public IntervalsViewModel(IMidiService midi, SettingsStore settings, [FromKeyedServices(StatsKeys.Intervals)] ExerciseStatsStore stats)
    {
        _midi = midi;
        _settings = settings;
        _stats = stats;

        var options = settings.Current.Intervals;
        foreach (var info in IntervalCatalog.All)
        {
            var choice = new IntervalChoice(info) { IsSelected = options.Intervals.Contains(info.Semitones) };
            choice.PropertyChanged += OnChoiceChanged;
            Choices.Add(choice);
        }
        SelectedDirectionIndex = (int)options.Direction;
        AnswerModeIndex = options.AnswerMode == IntervalAnswerMode.Name ? 1 : 0;
        QuestionsPerSession = options.QuestionsPerSession;
        SyncStageWithChoices();
        RefreshRecentSessions();

        _midi.NoteReceived += OnNoteReceived;
    }

    #region Phase

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup), nameof(IsRunning), nameof(IsSummary), nameof(IsKeyboardVisible), nameof(AreNameButtonsVisible))]
    public partial IntervalsPhase Phase { get; set; } = IntervalsPhase.Setup;

    public bool IsSetup => Phase == IntervalsPhase.Setup;
    public bool IsRunning => Phase == IntervalsPhase.Running;
    public bool IsSummary => Phase == IntervalsPhase.Summary;
    /// <summary>Keyboard while practising, and on the setup screen once an interval is being explained.</summary>
    public bool IsKeyboardVisible => IsRunning || (IsSetup && HasExplored);
    public bool AreNameButtonsVisible => IsRunning && IsNameMode;

    #endregion

    #region Setup

    public ObservableCollection<IntervalChoice> Choices { get; } = [];
    public IReadOnlyList<string> StageOptions { get; } = [.. IntervalOptions.Stages.Select(s => s.Name), "Custom"];
    public IReadOnlyList<string> DirectionOptions { get; } = ["Ascending", "Descending", "Together (harmonic)", "Mixed"];
    public ObservableCollection<string> RecentSessions { get; } = [];

    [ObservableProperty] public partial int SelectedStageIndex { get; set; }
    [ObservableProperty] public partial int SelectedDirectionIndex { get; set; }
    [ObservableProperty] public partial double QuestionsPerSession { get; set; }

    public IReadOnlyList<string> AnswerModeOptions { get; } = ["Play it back on the piano", "Name the interval"];

    /// <summary>0 = play back, 1 = name it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameMode), nameof(AreNameButtonsVisible))]
    public partial int AnswerModeIndex { get; set; }

    public bool IsNameMode => AnswerModeIndex == 1;

    [ObservableProperty] public partial bool CanStart { get; set; }

    partial void OnSelectedStageIndexChanged(int value)
    {
        if (value < 0 || value >= IntervalOptions.Stages.Count) return; // "Custom": leave the checkboxes alone
        _applyingStage = true;
        var set = IntervalOptions.Stages[value].Intervals;
        foreach (var choice in Choices) choice.IsSelected = set.Contains(choice.Semitones);
        _applyingStage = false;
        CanStart = Choices.Any(c => c.IsSelected);
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_applyingStage) SyncStageWithChoices();
    }

    private void SyncStageWithChoices()
    {
        var selected = Choices.Where(c => c.IsSelected).Select(c => c.Semitones).ToHashSet();
        var stage = IntervalOptions.Stages.ToList().FindIndex(s => selected.SetEquals(s.Intervals));
        SelectedStageIndex = stage >= 0 ? stage : StageOptions.Count - 1;
        CanStart = selected.Count > 0;
    }

    private IntervalOptions OptionsFromSetup() => _settings.Current.Intervals with
    {
        Intervals = Choices.Where(c => c.IsSelected).Select(c => c.Semitones).ToList(),
        Direction = (DirectionChoice)Math.Clamp(SelectedDirectionIndex, 0, 3),
        AnswerMode = IsNameMode ? IntervalAnswerMode.Name : IntervalAnswerMode.PlayBack,
        QuestionsPerSession = double.IsNaN(QuestionsPerSession) ? 20 : (int)Math.Clamp(QuestionsPerSession, 5, 200),
    };

    private void RefreshRecentSessions()
    {
        RecentSessions.Clear();
        foreach (var s in _stats.Data.Sessions.AsEnumerable().Reverse().Take(8))
        {
            RecentSessions.Add($"{s.Started.ToLocalTime():ddd d MMM HH:mm}  ·  {s.Accuracy:P0}  ·  {s.Options}");
        }
        if (RecentSessions.Count == 0) RecentSessions.Add("No sessions yet.");
    }

    #region Learn

    public IReadOnlyList<IntervalInfo> AllIntervals => IntervalCatalog.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeyboardVisible))]
    public partial bool HasExplored { get; set; }
    [ObservableProperty] public partial string ExploreTitle { get; set; } = "";
    [ObservableProperty] public partial string ExploreText { get; set; } = "";
    [ObservableProperty] public partial string ExploreHint { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<StaffNote> ExploreStaffNotes { get; set; } = [];

    /// <summary>Shows and plays an interval from middle C: one note after the other, then together.</summary>
    public async void Explore(int semitones)
    {
        var info = IntervalCatalog.Get(semitones);
        var top = IntervalCatalog.AboveMiddleC(semitones);
        var root = Pitch.MiddleC;

        HasExplored = true;
        ExploreTitle = $"{info.Short}  ·  {info.Name}";
        ExploreText = IntervalCatalog.Explain(semitones);
        ExploreHint = $"Up: “{info.AscendingSong}”   ·   Down: “{info.DescendingSong}”";
        ExploreStaffNotes = [new StaffNote(new SpelledPitch(Letter.C, 0, 4)), new StaffNote(top)];
        Highlights =
        [
            new KeyHighlight(root.MidiNumber, KeyHighlightKind.Sounded),
            .. Enumerable.Range(root.MidiNumber + 1, semitones - 1).Select(m => new KeyHighlight(m, KeyHighlightKind.Range, 0)),
            new KeyHighlight(top.Pitch.MidiNumber, KeyHighlightKind.Sounded),
        ];

        _playback?.Cancel();
        var cts = _playback = new CancellationTokenSource();
        try
        {
            await IntervalPlayer.PlayAsync(_midi, new IntervalQuestion(new IntervalItem(IntervalDirection.Ascending, semitones), root), cts.Token);
            await Task.Delay(250, cts.Token);
            await IntervalPlayer.PlayAsync(_midi, new IntervalQuestion(new IntervalItem(IntervalDirection.Harmonic, semitones), root), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Another interval was clicked, or the page was left.
        }
    }

    #endregion

    [RelayCommand]
    private void Start()
    {
        var options = OptionsFromSetup();
        if (options.Intervals.Count == 0) return;
        _settings.Update(s => s with { Intervals = options });

        _session = new IntervalSession(options, _stats, _random, TimeProvider.System);
        AnswerChoices = Choices.Where(c => c.IsSelected).Select(c => c.Info).ToList();
        Phase = IntervalsPhase.Running;
        AskCurrent();
    }

    #endregion

    #region Running

    [ObservableProperty] public partial IReadOnlyList<IntervalInfo> AnswerChoices { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyCollection<KeyHighlight> Highlights { get; set; } = [];
    [ObservableProperty] public partial string ProgressText { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string PromptText { get; set; } = "";
    [ObservableProperty] public partial string FeedbackText { get; set; } = "";
    [ObservableProperty] public partial string HintText { get; set; } = "";
    [ObservableProperty] public partial string ScoreText { get; set; } = "";

    private void AskCurrent()
    {
        if (_session is null) return;
        ProgressText = $"{_session.Number} / {_session.Total}";
        Progress = (_session.Number - 1) * 100.0 / _session.Total;
        ScoreText = _session.AnsweredCount == 0 ? "" : $"{_session.CorrectCount} / {_session.AnsweredCount} correct";
        FeedbackText = "";
        HintText = "";
        Highlights = [];
        _ = PlayCurrentAsync();
    }

    private async Task PlayCurrentAsync()
    {
        if (_session is null) return;
        _playback?.Cancel();
        var cts = _playback = new CancellationTokenSource();
        var question = _session.Current;

        PromptText = "Listen…";
        try
        {
            await IntervalPlayer.PlayAsync(_midi, question, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested || _session?.Current != question) return;

        _session.MarkPlayed();
        if (_session.State == CardState.Wrong)
        {
            PromptText = IsNameMode ? "Pick the right interval." : "Play the purple key.";
            return;
        }
        if (_session.State != CardState.Waiting)
        {
            PromptText = ""; // answered while it was still playing
            return;
        }

        if (IsNameMode)
        {
            PromptText = "Which interval was that?";
        }
        else
        {
            PromptText = question.Item.Direction == IntervalDirection.Harmonic
                ? "The lower note is lit. Play the upper note."
                : "The first note is lit. Play the second note.";
            Highlights = [new KeyHighlight(question.Root.MidiNumber, KeyHighlightKind.Sounded)];
        }
    }

    [RelayCommand]
    private void Replay() => _ = PlayCurrentAsync();

    private void OnNoteReceived(object? sender, MidiNoteEvent e)
    {
        if (e.IsNoteOn) _dispatcher.TryEnqueue(() => AnswerPlayed(e.Pitch));
    }

    public void ScreenKeyDown(int midi)
    {
        _midi.NoteOn(new Pitch(midi));
        AnswerPlayed(new Pitch(midi));
    }

    public void ScreenKeyUp(int midi) => _midi.NoteOff(new Pitch(midi));

    private void AnswerPlayed(Pitch played)
    {
        if (_session is null || !IsRunning || IsNameMode) return;
        var question = _session.Current;
        var outcome = _session.Answer(played);
        if (outcome == AnswerOutcome.Ignored) return;

        var root = new KeyHighlight(question.Root.MidiNumber, KeyHighlightKind.Sounded);
        Highlights = outcome is AnswerOutcome.Correct or AnswerOutcome.RetryCorrect
            ? [root, new KeyHighlight(played.MidiNumber, KeyHighlightKind.Correct)]
            : [root, new KeyHighlight(played.MidiNumber, KeyHighlightKind.Wrong), new KeyHighlight(question.Target.MidiNumber, KeyHighlightKind.Hint)];
        Show(outcome, question);
    }

    /// <summary>Name mode: an interval button was clicked.</summary>
    public void AnswerName(int semitones)
    {
        if (_session is null || !IsRunning || !IsNameMode) return;
        var question = _session.Current;
        var outcome = _session.AnswerName(semitones);
        if (outcome == AnswerOutcome.Ignored) return;

        Highlights =
        [
            new KeyHighlight(question.Root.MidiNumber, KeyHighlightKind.Sounded),
            new KeyHighlight(question.Target.MidiNumber, outcome is AnswerOutcome.Correct or AnswerOutcome.RetryCorrect ? KeyHighlightKind.Correct : KeyHighlightKind.Hint),
        ];
        if (outcome is AnswerOutcome.Wrong or AnswerOutcome.RetryWrong)
        {
            FeedbackText = $"✗  Not {IntervalCatalog.Get(semitones).Name}. Listen again and pick the right one.";
            HintText = SongHint(question);
            _ = PlayCurrentAsync();
            return;
        }
        Show(outcome, question);
    }

    private void Show(AnswerOutcome outcome, IntervalQuestion question)
    {
        if (_session is null) return;
        ScoreText = $"{_session.CorrectCount} / {_session.AnsweredCount} correct";

        switch (outcome)
        {
            case AnswerOutcome.Correct:
                FeedbackText = $"✓  {question.Item}  ·  {_session.LastAnswerTime.TotalSeconds:F1} s";
                HintText = SongHint(question);
                PromptText = "";
                ScheduleAdvance();
                break;
            case AnswerOutcome.RetryCorrect:
                FeedbackText = $"✓  {question.Item}";
                PromptText = "";
                ScheduleAdvance();
                break;
            case AnswerOutcome.Wrong:
            case AnswerOutcome.RetryWrong:
                var why = outcome == AnswerOutcome.Wrong && !IsNameMode && _session.Mistakes.Count > 0
                    ? Enum.Parse<IntervalMistakeKind>(_session.Mistakes[^1].Kind) switch
                    {
                        IntervalMistakeKind.NearMiss => "Close!  ",
                        IntervalMistakeKind.WrongOctave => "Right note, wrong octave.  ",
                        IntervalMistakeKind.WrongDirection => "Right size, wrong direction.  ",
                        _ => "",
                    }
                    : "";
                FeedbackText = $"✗  {why}It was a {question.Item.ToString().ToLowerInvariant()}. Find the purple key.";
                HintText = SongHint(question);
                break;
        }
    }

    private static string SongHint(IntervalQuestion q) => q.Item.Direction switch
    {
        IntervalDirection.Descending => $"Think: “{q.Item.Info.DescendingSong}”",
        _ => $"Think: “{q.Item.Info.AscendingSong}”",
    };

    private async void ScheduleAdvance()
    {
        var token = ++_advanceToken;
        await Task.Delay(CorrectPause);
        if (token == _advanceToken) Next();
    }

    private void Next()
    {
        if (_session is null) return;
        _session.Advance();
        if (_session.IsFinished) ShowSummary();
        else AskCurrent();
    }

    [RelayCommand]
    private void Skip()
    {
        _advanceToken++;
        _playback?.Cancel();
        Next();
    }

    [RelayCommand]
    private void Stop()
    {
        _advanceToken++;
        _playback?.Cancel();
        if (_session is null) return;
        _session.Finish();
        if (_session.AnsweredCount > 0) ShowSummary();
        else BackToSetup();
    }

    #endregion

    #region Summary

    [ObservableProperty] public partial string SummaryAccuracy { get; set; } = "";
    [ObservableProperty] public partial string SummaryDetail { get; set; } = "";
    public ObservableCollection<string> PerInterval { get; } = [];

    private void ShowSummary()
    {
        if (_session is null) return;
        var summary = _session.Summary;
        SummaryAccuracy = $"{summary.Accuracy:P0}";
        SummaryDetail = $"{summary.Correct} of {summary.Cards} right first time  ·  {summary.Options}";

        PerInterval.Clear();
        foreach (var item in _session.Items.OrderBy(i => _stats.Data.For(i.Id)?.SmoothedAccuracy ?? 1))
        {
            var s = _stats.Data.For(item.Id);
            PerInterval.Add(s is null || s.Attempts == 0
                ? $"{item}  ·  not asked yet"
                : $"{item}  ·  {s.Correct}/{s.Attempts} right ({s.Accuracy:P0})");
        }

        SessionMistakes.Clear();
        var mistakes = _session.Mistakes;
        MistakeBreakdown = mistakes.Count == 0
            ? "No mistakes this session."
            : string.Join("\n", mistakes
                .GroupBy(m => Enum.Parse<IntervalMistakeKind>(m.Kind))
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} × {IntervalMistakes.Describe(g.Key)}"));
        foreach (var m in mistakes) SessionMistakes.Add($"{m.Expected}   ·   you: {m.Given}");

        Highlights = [];
        Phase = IntervalsPhase.Summary;
    }

    [ObservableProperty] public partial string MistakeBreakdown { get; set; } = "";
    public ObservableCollection<string> SessionMistakes { get; } = [];

    [RelayCommand]
    private void Again() => Start();

    [RelayCommand]
    private void BackToSetup()
    {
        _playback?.Cancel();
        _session = null;
        Highlights = [];
        RefreshRecentSessions();
        Phase = IntervalsPhase.Setup;
    }

    #endregion

    public void Dispose()
    {
        _advanceToken++;
        _playback?.Cancel();
        _midi.NoteReceived -= OnNoteReceived;
        foreach (var choice in Choices) choice.PropertyChanged -= OnChoiceChanged;
        if (_session is { IsFinished: false }) _session.Finish();
    }
}
