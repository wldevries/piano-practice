using PianoPractice.Core.Midi;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Exercises.Intervals;

/// <summary>What gets practised and tracked: an interval size in one direction.</summary>
public sealed record IntervalItem(IntervalDirection Direction, int Semitones)
{
    public string Id => $"{Direction}:{Semitones}";
    public IntervalInfo Info => IntervalCatalog.Get(Semitones);

    public override string ToString() => Direction switch
    {
        IntervalDirection.Ascending => $"{Info.Name} up",
        IntervalDirection.Descending => $"{Info.Name} down",
        _ => $"{Info.Name} together",
    };
}

/// <summary>One concrete question: an item placed on actual notes.</summary>
public sealed record IntervalQuestion(IntervalItem Item, Pitch Root)
{
    /// <summary>The note the player has to find: above the root, or below it when descending.</summary>
    public Pitch Target => Item.Direction == IntervalDirection.Descending
        ? Root.Transpose(-Item.Semitones)
        : Root.Transpose(Item.Semitones);

    /// <summary>Notes in playing order (both at once when harmonic).</summary>
    public (Pitch First, Pitch Second) Notes => (Root, Target);
}

/// <summary>
/// An interval ear-training run. In play-back mode the root is given and the player finds the
/// other note (playing the root again is ignored, people do that to orient themselves).
/// In name mode the player picks the interval name. First answer scores; then retry until right.
/// </summary>
public sealed class IntervalSession
{
    public const string StatsFileName = "interval-stats.json";

    private readonly IntervalOptions _options;
    private readonly ExerciseStatsStore _store;
    private readonly Random _random;
    private readonly TimeProvider _time;
    private readonly WeightedPicker<IntervalItem> _picker;
    private readonly DateTimeOffset _started;
    private readonly List<double> _answerTimes = [];
    private long _askedAt;

    public IntervalSession(IntervalOptions options, ExerciseStatsStore store, Random random, TimeProvider time)
    {
        _options = options;
        _store = store;
        _random = random;
        _time = time;
        _started = time.GetUtcNow();

        Items = options.Directions
            .SelectMany(d => options.Intervals.Where(s => s is >= 1 and <= 12).Distinct().Order().Select(s => new IntervalItem(d, s)))
            .ToList();
        if (Items.Count == 0) throw new ArgumentException("Choose at least one interval.", nameof(options));

        _picker = new WeightedPicker<IntervalItem>(Items, i => i.Id, store.Data, random);
        Current = MakeQuestion(_picker.Next(null));
        _askedAt = time.GetTimestamp();
    }

    public IReadOnlyList<IntervalItem> Items { get; }
    public IntervalOptions Options => _options;
    public IntervalQuestion Current { get; private set; }
    public CardState State { get; private set; } = CardState.Waiting;
    public int Number { get; private set; } = 1;
    public int Total => _options.QuestionsPerSession;
    public int CorrectCount { get; private set; }
    public int AnsweredCount => _answerTimes.Count;
    public bool IsFinished { get; private set; }
    public TimeSpan LastAnswerTime { get; private set; }

    private IntervalQuestion MakeQuestion(IntervalItem item)
    {
        // Pick the lower note of the pair in range, then place the root accordingly.
        var low = Math.Max(_options.LowestNote, Pitch.PianoLowest);
        var high = Math.Max(low, Math.Min(_options.HighestNote, Pitch.PianoHighest - item.Semitones));
        var lower = new Pitch(_random.Next(low, high + 1));
        var root = item.Direction == IntervalDirection.Descending ? lower.Transpose(item.Semitones) : lower;
        return new IntervalQuestion(item, root);
    }

    /// <summary>Call when the interval has finished playing: answer time is measured from here.</summary>
    public void MarkPlayed()
    {
        if (State == CardState.Waiting) _askedAt = _time.GetTimestamp();
    }

    /// <summary>Play-back mode: a note played on the piano.</summary>
    public AnswerOutcome Answer(Pitch played) =>
        played == Current.Root ? AnswerOutcome.Ignored : Judge(played == Current.Target);

    /// <summary>Name mode: the interval size picked.</summary>
    public AnswerOutcome AnswerName(int semitones) => Judge(semitones == Current.Item.Semitones);

    private AnswerOutcome Judge(bool correct)
    {
        if (IsFinished) return AnswerOutcome.Ignored;
        switch (State)
        {
            case CardState.Waiting:
                LastAnswerTime = _time.GetElapsedTime(_askedAt);
                _answerTimes.Add(LastAnswerTime.TotalMilliseconds);
                _store.Data.GetOrAdd(Current.Item.Id).Record(correct, LastAnswerTime.TotalMilliseconds, _time.GetUtcNow());
                if (correct)
                {
                    CorrectCount++;
                    State = CardState.Correct;
                    return AnswerOutcome.Correct;
                }
                State = CardState.Wrong;
                return AnswerOutcome.Wrong;

            case CardState.Wrong when correct:
                State = CardState.Correct;
                return AnswerOutcome.RetryCorrect;

            case CardState.Wrong:
                return AnswerOutcome.RetryWrong;

            default:
                return AnswerOutcome.Ignored;
        }
    }

    public void Advance()
    {
        if (IsFinished) return;
        if (Number >= Total)
        {
            Finish();
            return;
        }
        Number++;
        Current = MakeQuestion(_picker.Next(Current.Item));
        State = CardState.Waiting;
        _askedAt = _time.GetTimestamp();
    }

    public SessionSummary Finish()
    {
        if (!IsFinished)
        {
            IsFinished = true;
            if (AnsweredCount > 0) _store.AddSession(Summary);
            else _store.Save();
        }
        return Summary;
    }

    public SessionSummary Summary => new(
        _started,
        _options.Describe(),
        AnsweredCount,
        CorrectCount,
        _answerTimes.Count == 0 ? 0 : _answerTimes.Average());
}

/// <summary>Plays interval questions on the piano.</summary>
public static class IntervalPlayer
{
    private static readonly TimeSpan NoteLength = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan HarmonicLength = TimeSpan.FromMilliseconds(1400);
    private const int Velocity = 72;

    public static async Task PlayAsync(IMidiService midi, IntervalQuestion question, CancellationToken cancellationToken = default)
    {
        var (first, second) = question.Notes;
        if (question.Item.Direction == IntervalDirection.Harmonic)
        {
            midi.NoteOn(first, Velocity);
            midi.NoteOn(second, Velocity);
            try
            {
                await Task.Delay(HarmonicLength, cancellationToken);
            }
            finally
            {
                midi.NoteOff(first);
                midi.NoteOff(second);
            }
            return;
        }

        await midi.PlayNoteAsync(first, NoteLength, Velocity, cancellationToken);
        await Task.Delay(Gap, cancellationToken);
        await midi.PlayNoteAsync(second, NoteLength, Velocity, cancellationToken);
    }
}
