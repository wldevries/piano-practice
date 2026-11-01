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

    [Fact]
    public void Empty_interval_set_is_rejected()
        => Assert.Throws<ArgumentException>(() => Start(new IntervalOptions { Intervals = [] }));
}
