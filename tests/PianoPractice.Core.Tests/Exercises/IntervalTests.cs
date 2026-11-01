using PianoPractice.Core.Exercises;
using PianoPractice.Core.Exercises.Intervals;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Exercises;

public class IntervalTests
{
    private static (IntervalSession Session, FakeTime Time, ExerciseStatsStore Store) Start(IntervalOptions? options = null, int seed = 3)
    {
        var time = new FakeTime();
        var store = ExerciseStatsStore.InMemory();
        return (new IntervalSession(options ?? new IntervalOptions { QuestionsPerSession = 3 }, store, new Random(seed), time), time, store);
    }

    [Fact]
    public void Catalog_covers_all_simple_intervals()
    {
        Assert.Equal(Enumerable.Range(1, 12), IntervalCatalog.All.Select(i => i.Semitones));
        Assert.Equal("P5", IntervalCatalog.Get(7).Short);
        Assert.Throws<ArgumentOutOfRangeException>(() => IntervalCatalog.Get(13));
    }

    [Fact]
    public void Intervals_above_middle_c_are_spelled_conventionally_and_have_the_right_size()
    {
        Assert.Equal(["Db4", "D4", "Eb4", "E4", "F4", "F#4", "G4", "Ab4", "A4", "Bb4", "B4", "C5"],
            IntervalCatalog.All.Select(i => IntervalCatalog.AboveMiddleC(i.Semitones).ToString()));
        foreach (var i in IntervalCatalog.All)
        {
            Assert.Equal(60 + i.Semitones, IntervalCatalog.AboveMiddleC(i.Semitones).Pitch.MidiNumber);
        }
    }

    [Fact]
    public void Explanation_counts_half_steps_and_letters()
    {
        Assert.Equal("7 half steps  ·  C → G  ·  letters C D E F G", IntervalCatalog.Explain(7));
        Assert.Equal("3 half steps  ·  C → E♭  ·  letters C D E", IntervalCatalog.Explain(3));
        Assert.Equal("12 half steps  ·  C → C  ·  letters C D E F G A B C", IntervalCatalog.Explain(12));
    }

    [Fact]
    public void Mixed_direction_practises_every_interval_in_every_direction()
    {
        var (session, _, _) = Start(new IntervalOptions { Intervals = [7, 12], Direction = DirectionChoice.Mixed });
        Assert.Equal(6, session.Items.Count);
    }

    [Theory]
    [InlineData(DirectionChoice.Ascending)]
    [InlineData(DirectionChoice.Descending)]
    [InlineData(DirectionChoice.Harmonic)]
    public void Questions_stay_in_range_and_have_the_right_size(DirectionChoice direction)
    {
        var options = new IntervalOptions
        {
            Intervals = [1, 5, 11, 12], Direction = direction, QuestionsPerSession = 200, LowestNote = 48, HighestNote = 72,
        };
        var (session, _, _) = Start(options);
        for (var i = 0; i < 200; i++)
        {
            var q = session.Current;
            var low = Math.Min(q.Root.MidiNumber, q.Target.MidiNumber);
            var high = Math.Max(q.Root.MidiNumber, q.Target.MidiNumber);
            Assert.Equal(q.Item.Semitones, high - low);
            Assert.InRange(low, 48, 72);
            if (direction == DirectionChoice.Descending) Assert.True(q.Target < q.Root);
            else Assert.True(q.Target > q.Root);
            session.AnswerName(q.Item.Semitones);
            session.Advance();
        }
    }

    [Fact]
    public void Play_back_ignores_the_root_and_judges_the_other_note()
    {
        var (session, time, store) = Start();
        var q = session.Current;
        session.MarkPlayed();
        time.Advance(TimeSpan.FromMilliseconds(900));

        Assert.Equal(AnswerOutcome.Ignored, session.Answer(q.Root));
        Assert.Equal(AnswerOutcome.Correct, session.Answer(q.Target));
        Assert.Equal(900, session.LastAnswerTime.TotalMilliseconds, precision: 3);
        Assert.Equal(1, store.Data.For(q.Item.Id)!.Correct);
    }

    [Fact]
    public void Wrong_then_retry()
    {
        var (session, _, store) = Start();
        var q = session.Current;

        Assert.Equal(AnswerOutcome.Wrong, session.Answer(q.Target.Transpose(1)));
        Assert.Equal(AnswerOutcome.RetryWrong, session.Answer(q.Target.Transpose(-1)));
        Assert.Equal(AnswerOutcome.RetryCorrect, session.Answer(q.Target));
        Assert.Equal(0, session.CorrectCount);
        Assert.Equal((1, 0), (store.Data.For(q.Item.Id)!.Attempts, store.Data.For(q.Item.Id)!.Correct));
    }

    [Fact]
    public void Name_mode_judges_the_size()
    {
        var (session, _, _) = Start(new IntervalOptions { Intervals = [4, 7], AnswerMode = IntervalAnswerMode.Name });
        var size = session.Current.Item.Semitones;
        Assert.Equal(AnswerOutcome.Wrong, session.AnswerName(size == 7 ? 4 : 7));
        Assert.Equal(AnswerOutcome.RetryCorrect, session.AnswerName(size));
    }

    [Fact]
    public void Session_is_recorded_when_finished()
    {
        var (session, _, store) = Start();
        for (var i = 0; i < 3; i++)
        {
            session.Answer(session.Current.Target);
            session.Advance();
        }
        Assert.True(session.IsFinished);
        var summary = Assert.Single(store.Data.Sessions);
        Assert.Equal((3, 3), (summary.Cards, summary.Correct));
        Assert.Contains("P5 P8", summary.Options);
    }

    private static readonly int[] FifthAndOctave = [7, 12];

    private static IntervalQuestion Up(int semitones, int root = 60) =>
        new(new IntervalItem(IntervalDirection.Ascending, semitones), new Pitch(root));

    [Theory]
    [InlineData(72, IntervalMistakeKind.OtherInterval, 12)] // played the octave instead of the fifth
    [InlineData(66, IntervalMistakeKind.NearMiss, 6)]
    [InlineData(68, IntervalMistakeKind.NearMiss, 8)]
    [InlineData(55, IntervalMistakeKind.WrongOctave, -5)]   // G3 instead of G4
    [InlineData(53, IntervalMistakeKind.WrongDirection, -7)]
    [InlineData(63, IntervalMistakeKind.Other, 3)]
    public void Wrong_keys_are_classified(int played, IntervalMistakeKind kind, int semis)
        => Assert.Equal((kind, semis), IntervalMistakes.Classify(Up(7), new Pitch(played), FifthAndOctave));

    [Fact]
    public void Descending_mistakes_are_measured_downwards()
    {
        var q = new IntervalQuestion(new IntervalItem(IntervalDirection.Descending, 7), new Pitch(67)); // G4 down to C4
        Assert.Equal((IntervalMistakeKind.OtherInterval, 12), IntervalMistakes.Classify(q, new Pitch(55), FifthAndOctave));
        Assert.Equal((IntervalMistakeKind.NearMiss, 6), IntervalMistakes.Classify(q, new Pitch(61), FifthAndOctave));
    }

    [Fact]
    public void Only_first_wrong_answer_is_recorded_as_mistake()
    {
        var (session, _, store) = Start();
        var target = session.Current.Target;
        session.Answer(target.Transpose(1));
        session.Answer(target.Transpose(2)); // retry, not recorded
        session.Answer(target);

        var mistake = Assert.Single(session.Mistakes);
        Assert.Equal(session.Current.Item.Id, mistake.ItemId);
        Assert.Equal(nameof(IntervalMistakeKind.NearMiss), mistake.Kind);
        Assert.Contains(NoteNames.WithOctave(target.Transpose(1)), mistake.Given);
        Assert.Single(store.Data.Mistakes);
    }

    [Fact]
    public void Empty_interval_set_is_rejected()
        => Assert.Throws<ArgumentException>(() => Start(new IntervalOptions { Intervals = [] }));
}
