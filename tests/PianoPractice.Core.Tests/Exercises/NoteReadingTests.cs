using PianoPractice.Core.Exercises;
using PianoPractice.Core.Exercises.NoteReading;
using PianoPractice.Core.Notation;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Exercises;

/// <summary>Manually advanced clock.</summary>
internal sealed class FakeTime : TimeProvider
{
    private long _ticks;
    public void Advance(TimeSpan by) => _ticks += by.Ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(_ticks);
}

public class NoteReadingDeckTests
{
    [Fact]
    public void Treble_staff_only_in_C_has_the_nine_staff_notes()
    {
        var deck = NoteReadingDeck.Build(new NoteReadingOptions());
        Assert.Equal(["E4", "F4", "G4", "A4", "B4", "C5", "D5", "E5", "F5"], deck.Select(c => c.Pitch.ToString()));
        Assert.All(deck, c => Assert.Equal(Clef.Treble, c.Clef));
    }

    [Fact]
    public void Ledger_lines_extend_the_range()
    {
        var deck = NoteReadingDeck.Build(new NoteReadingOptions { Clefs = ClefChoice.Bass, LedgerLines = 1 });
        Assert.Equal("E2", deck[0].Pitch.ToString());
        Assert.Equal("C4", deck[^1].Pitch.ToString());
        Assert.Equal(13, deck.Count);
    }

    [Fact]
    public void Key_signature_applies_to_the_cards()
    {
        var deck = NoteReadingDeck.Build(new NoteReadingOptions { KeyFifths = 1 });
        Assert.Contains(deck, c => c.Pitch.ToString() == "F#4");
        Assert.DoesNotContain(deck, c => c.Pitch.ToString() == "F4");
    }

    [Fact]
    public void Accidentals_add_sharp_and_flat_variants_relative_to_the_key()
    {
        var deck = NoteReadingDeck.Build(new NoteReadingOptions { IncludeAccidentals = true });
        var names = deck.Select(c => c.Pitch.ToString()).ToHashSet();
        Assert.Contains("F#4", names);
        Assert.Contains("Gb4", names);
        Assert.Contains("G4", names);
        Assert.Equal(27, deck.Count);

        // In G major, F# is in the key: variants are F (natural) and F## is excluded.
        var g = NoteReadingDeck.Build(new NoteReadingOptions { KeyFifths = 1, IncludeAccidentals = true });
        Assert.Contains(g, c => c.Pitch.ToString() == "F4");
        Assert.DoesNotContain(g, c => c.Pitch.Accidental == 2);
    }

    [Fact]
    public void Both_clefs_include_middle_c_region_on_each_staff()
    {
        var deck = NoteReadingDeck.Build(new NoteReadingOptions { Clefs = ClefChoice.Both, LedgerLines = 1 });
        Assert.Contains(new NoteCard(new SpelledPitch(Letter.C, 0, 4), Clef.Treble), deck);
        Assert.Contains(new NoteCard(new SpelledPitch(Letter.C, 0, 4), Clef.Bass), deck);
    }

    [Theory]
    [InlineData("Treble:F#4")]
    [InlineData("Bass:Bb2")]
    [InlineData("Treble:C4")]
    public void Card_ids_round_trip(string id) => Assert.Equal(id, NoteCard.TryParseId(id)!.Id);
}

public class WeightedPickerTests
{
    [Fact]
    public void Weights_favour_new_wrong_and_slow_cards()
    {
        var good = new CardStats { Attempts = 10, Correct = 10, AverageMs = 800 };
        var wrong = new CardStats { Attempts = 10, Correct = 3, AverageMs = 800 };
        var slow = new CardStats { Attempts = 10, Correct = 10, AverageMs = 3500 };

        Assert.True(PracticeWeight.Of(wrong) > PracticeWeight.Of(good));
        Assert.True(PracticeWeight.Of(slow) > PracticeWeight.Of(good));
        Assert.True(PracticeWeight.Of(null) > PracticeWeight.Of(good));
    }

    [Fact]
    public void Slowness_is_relative_to_the_players_own_pace()
    {
        // A beginner averaging ~6 s: 10 s notes must still stand out from 3 s notes.
        var stats = new ExerciseStats();
        var deck = NoteReadingDeck.Build(new NoteReadingOptions());
        double[] times = [3000, 5000, 6000, 6300, 6800, 8300, 9800, 10000, 2800];
        for (var i = 0; i < deck.Count; i++) stats.GetOrAdd(deck[i].Id).Record(true, times[i], DateTimeOffset.UnixEpoch);

        var reference = PracticeWeight.ReferenceMs(stats);
        Assert.Equal(6300, reference);
        Assert.True(PracticeWeight.Of(stats.For(deck[7].Id), reference) > PracticeWeight.Of(stats.For(deck[0].Id), reference) + 0.5);

        // With a fixed 1.5 s bar both would be capped as "maximally slow".
        Assert.Equal(PracticeWeight.Of(stats.For(deck[7].Id)), PracticeWeight.Of(stats.For(deck[2].Id)));
    }

    [Fact]
    public void Reference_never_drops_below_the_comfortable_pace()
    {
        var stats = new ExerciseStats();
        foreach (var card in NoteReadingDeck.Build(new NoteReadingOptions())) stats.GetOrAdd(card.Id).Record(true, 700, DateTimeOffset.UnixEpoch);
        Assert.Equal(PracticeWeight.ComfortableMs, PracticeWeight.ReferenceMs(stats));
        Assert.Equal(PracticeWeight.ComfortableMs, PracticeWeight.ReferenceMs(new ExerciseStats()));
    }

    [Fact]
    public void Weak_cards_come_up_more_often()
    {
        var deck = NoteReadingDeck.Build(new NoteReadingOptions());
        var stats = new ExerciseStats();
        foreach (var card in deck)
        {
            for (var i = 0; i < 10; i++) stats.GetOrAdd(card.Id).Record(true, 600, DateTimeOffset.UnixEpoch);
        }
        var weak = deck[3];
        var weakStats = stats.GetOrAdd(weak.Id);
        for (var i = 0; i < 10; i++) weakStats.Record(false, 3000, DateTimeOffset.UnixEpoch);

        var picker = new WeightedPicker<NoteCard>(deck, c => c.Id, stats, new Random(42));
        var counts = deck.ToDictionary(c => c, _ => 0);
        NoteCard? previous = null;
        for (var i = 0; i < 5000; i++)
        {
            previous = picker.Next(previous);
            counts[previous]++;
        }

        var othersAverage = counts.Where(kv => kv.Key != weak).Average(kv => kv.Value);
        Assert.True(counts[weak] > 2 * othersAverage, $"weak={counts[weak]}, others≈{othersAverage:F0}");
    }

    [Fact]
    public void Never_repeats_the_previous_card()
    {
        var deck = NoteReadingDeck.Build(new NoteReadingOptions());
        var picker = new WeightedPicker<NoteCard>(deck, c => c.Id, new ExerciseStats(), new Random(1));
        var previous = picker.Next(null);
        for (var i = 0; i < 500; i++)
        {
            var next = picker.Next(previous);
            Assert.NotEqual(previous, next);
            previous = next;
        }
    }
}

public class NoteReadingSessionTests
{
    private static (NoteReadingSession Session, FakeTime Time, ExerciseStatsStore Store) Start(NoteReadingOptions? options = null)
    {
        var time = new FakeTime();
        var store = ExerciseStatsStore.InMemory();
        return (new NoteReadingSession(options ?? new NoteReadingOptions { CardsPerSession = 3 }, store, new Random(7), time), time, store);
    }

    [Fact]
    public void Correct_answer_is_scored_and_timed()
    {
        var (session, time, store) = Start();
        time.Advance(TimeSpan.FromMilliseconds(1200));

        Assert.Equal(AnswerOutcome.Correct, session.Answer(session.Current.Pitch.Pitch));
        Assert.Equal(1, session.CorrectCount);
        Assert.Equal(1200, session.LastAnswerTime.TotalMilliseconds, precision: 3);
        Assert.Equal(1, store.Data.For(session.Current.Id)!.Correct);
    }

    [Fact]
    public void Wrong_answer_requires_the_right_note_but_only_the_first_answer_counts()
    {
        var (session, _, store) = Start();
        var target = session.Current.Pitch.Pitch;

        Assert.Equal(AnswerOutcome.Wrong, session.Answer(target.Transpose(1)));
        Assert.Equal(AnswerOutcome.RetryWrong, session.Answer(target.Transpose(2)));
        Assert.Equal(AnswerOutcome.RetryCorrect, session.Answer(target));
        Assert.Equal(AnswerOutcome.Ignored, session.Answer(target));

        Assert.Equal(0, session.CorrectCount);
        Assert.Equal(1, session.AnsweredCount);
        var stats = store.Data.For(session.Current.Id)!;
        Assert.Equal((1, 0), (stats.Attempts, stats.Correct));
    }

    [Fact]
    public void Octave_can_be_optional()
    {
        var (session, _, _) = Start(new NoteReadingOptions { RequireExactOctave = false });
        Assert.Equal(AnswerOutcome.Correct, session.Answer(session.Current.Pitch.Pitch.Transpose(12)));

        var (strict, _, _) = Start();
        Assert.Equal(AnswerOutcome.Wrong, strict.Answer(strict.Current.Pitch.Pitch.Transpose(12)));
    }

    [Fact]
    public void Finishes_after_the_last_card_and_records_the_session()
    {
        var (session, time, store) = Start();
        for (var i = 0; i < 3; i++)
        {
            Assert.False(session.IsFinished);
            Assert.Equal(i + 1, session.Number);
            time.Advance(TimeSpan.FromSeconds(1));
            session.Answer(i == 1 ? session.Current.Pitch.Pitch.Transpose(1) : session.Current.Pitch.Pitch);
            session.Answer(session.Current.Pitch.Pitch); // retry after the wrong one
            session.Advance();
        }

        Assert.True(session.IsFinished);
        var summary = Assert.Single(store.Data.Sessions);
        Assert.Equal((3, 2), (summary.Cards, summary.Correct));
        Assert.Equal(1000, summary.AverageMs, precision: 3);
    }

    [Fact]
    public void Stopping_without_answers_records_no_session()
    {
        var (session, _, store) = Start();
        session.Finish();
        Assert.Empty(store.Data.Sessions);
    }
}
