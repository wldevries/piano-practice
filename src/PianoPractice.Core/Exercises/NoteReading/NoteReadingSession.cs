using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Exercises.NoteReading;

/// <summary>
/// One run of flashcards. Only the first answer to a card is scored; after a wrong answer
/// the player has to find the right key before moving on (unscored), which reinforces it.
/// </summary>
public sealed class NoteReadingSession
{
    public const string StatsFileName = "note-reading-stats.json";

    private readonly NoteReadingOptions _options;
    private readonly WeightedPicker<NoteCard> _picker;
    private readonly ExerciseStatsStore _store;
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _started;
    private readonly List<double> _answerTimes = [];
    private long _shownAt;

    public NoteReadingSession(NoteReadingOptions options, ExerciseStatsStore store, Random random, TimeProvider time)
    {
        _options = options;
        _store = store;
        _time = time;
        _started = time.GetUtcNow();
        Deck = NoteReadingDeck.Build(options);
        _picker = new WeightedPicker<NoteCard>(Deck, c => c.Id, store.Data, random);
        Current = _picker.Next(null);
        _shownAt = time.GetTimestamp();
    }

    public IReadOnlyList<NoteCard> Deck { get; }
    public NoteCard Current { get; private set; }
    public CardState State { get; private set; } = CardState.Waiting;

    /// <summary>1-based number of the current card.</summary>
    public int Number { get; private set; } = 1;
    public int Total => _options.CardsPerSession;
    public int CorrectCount { get; private set; }
    public int AnsweredCount => _answerTimes.Count;
    public bool IsFinished { get; private set; }

    /// <summary>Time taken for the last scored answer.</summary>
    public TimeSpan LastAnswerTime { get; private set; }

    public bool Matches(Pitch played) => _options.RequireExactOctave
        ? played == Current.Pitch.Pitch
        : played.PitchClass == Current.Pitch.Pitch.PitchClass;

    public AnswerOutcome Answer(Pitch played)
    {
        if (IsFinished) return AnswerOutcome.Ignored;

        switch (State)
        {
            case CardState.Waiting:
                LastAnswerTime = _time.GetElapsedTime(_shownAt);
                var correct = Matches(played);
                _answerTimes.Add(LastAnswerTime.TotalMilliseconds);
                _store.Data.GetOrAdd(Current.Id).Record(correct, LastAnswerTime.TotalMilliseconds, _time.GetUtcNow());
                if (correct)
                {
                    CorrectCount++;
                    State = CardState.Correct;
                    return AnswerOutcome.Correct;
                }
                State = CardState.Wrong;
                return AnswerOutcome.Wrong;

            case CardState.Wrong when Matches(played):
                State = CardState.Correct;
                return AnswerOutcome.RetryCorrect;

            case CardState.Wrong:
                return AnswerOutcome.RetryWrong;

            default:
                return AnswerOutcome.Ignored;
        }
    }

    /// <summary>Moves to the next card, or finishes the session after the last one.</summary>
    public void Advance()
    {
        if (IsFinished) return;
        if (Number >= Total)
        {
            Finish();
            return;
        }
        Number++;
        Current = _picker.Next(Current);
        State = CardState.Waiting;
        _shownAt = _time.GetTimestamp();
    }

    /// <summary>Ends the session early (or after the last card) and saves the results.</summary>
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
