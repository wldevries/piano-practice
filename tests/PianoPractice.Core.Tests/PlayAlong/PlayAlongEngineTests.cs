using PianoPractice.Core.PlayAlong;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.PlayAlong;

public class PlayAlongEngineTests
{
    // One beat per second. Chords for both hands at 0 (C4+C3), 1, 2 (E4+G2), 3, 4 (G4+C3).
    private static readonly Song TwoBars = SongText.Parse("""
        tempo: 60
        RH: C4/q D4 E4 F4 | G4/w |
        LH: C3/h G2/h | C3/w |
        """);

    private static Pitch P(string name) => SpelledPitch.TryParse(name, out var p) ? p.Pitch : throw new ArgumentException(name);

    private static (PlayAlongEngine Engine, List<NoteAction> Out) Start(PlayAlongSetup? setup = null) =>
        (new PlayAlongEngine(TwoBars, setup ?? new PlayAlongSetup()), []);

    [Fact]
    public void Starts_with_a_lead_in_before_the_first_bar()
    {
        var (engine, _) = Start(new PlayAlongSetup { LeadInSeconds = 2, Tempo = 0.5 });
        Assert.Equal(-1, engine.Position); // 2 real seconds at half speed = 1 song second
    }

    [Fact]
    public void Wait_mode_holds_at_a_chord_until_all_its_notes_are_played()
    {
        var (engine, output) = Start();
        engine.Update(5, output);
        Assert.Equal(0, engine.Position);
        Assert.True(engine.IsWaiting);
        Assert.Equal([P("C3"), P("C4")], engine.Expected.Order());

        Assert.Equal(PressResult.Correct, engine.Press(P("C4")));
        Assert.True(engine.IsWaiting);
        engine.Update(1, output);
        Assert.Equal(0, engine.Position);

        Assert.Equal(PressResult.Correct, engine.Press(P("C3")));
        Assert.False(engine.IsWaiting);
        engine.Update(0.5, output);
        Assert.Equal(0.5, engine.Position);
        Assert.Equal([P("D4")], engine.Expected);
    }

    [Fact]
    public void A_wrong_note_makes_the_chord_not_clean()
    {
        var (engine, output) = Start(new PlayAlongSetup { Hands = PracticeHands.Right });
        engine.Update(5, output);
        Assert.Equal(PressResult.Wrong, engine.Press(P("D4")));
        engine.Press(P("C4"));
        engine.Update(5, output);
        engine.Press(P("D4"));

        Assert.Equal(2, engine.Result.Chords);
        Assert.Equal(1, engine.Result.CleanChords);
        Assert.Equal(1, engine.Result.WrongNotes);
        Assert.Equal(0.5, engine.Result.Accuracy(PlayAlongMode.Wait));
    }

    [Fact]
    public void Notes_can_be_played_a_little_early()
    {
        var (engine, output) = Start(new PlayAlongSetup { Hands = PracticeHands.Right });
        engine.Update(1.9, output); // 0.1 s before the first note
        Assert.Equal(PressResult.Correct, engine.Press(P("C4")));
        engine.Update(0.2, output);
        Assert.False(engine.IsWaiting);
        Assert.Equal(0.1, engine.Position, 6);
    }

    [Fact]
    public void Playing_far_too_early_is_wrong()
    {
        var (engine, _) = Start(new PlayAlongSetup { Hands = PracticeHands.Right });
        Assert.Equal(PressResult.Wrong, engine.Press(P("C4")));
        Assert.Equal(0, engine.Result.Chords);
    }

    [Fact]
    public void The_other_hand_waits_for_the_player_then_sounds_on_the_piano()
    {
        var (engine, output) = Start(new PlayAlongSetup { Hands = PracticeHands.Right });
        engine.Update(5, output);
        Assert.True(engine.IsWaiting);
        Assert.Empty(output); // C3 is held back until the player plays C4

        engine.Press(P("C4"));
        engine.Update(0.01, output);
        Assert.Equal([new NoteAction(P("C3"), 80, true, Hand.Left)], output);

        output.Clear();
        engine.Update(1.5, output); // waits at D4 (1.0)
        engine.Press(P("D4"));
        engine.Update(1.5, output); // waits at E4 (2.0); C3 ends there
        engine.Press(P("E4"));
        engine.Update(0.01, output);
        Assert.Equal([new NoteAction(P("C3"), 0, false, Hand.Left), new NoteAction(P("G2"), 80, true, Hand.Left)], output);
        Assert.Equal(NoteStatus.Accompaniment, engine.StatusOf(0)); // C3 at 0
    }

    [Fact]
    public void A_muted_other_hand_is_not_played()
    {
        var (engine, output) = Start(new PlayAlongSetup { Hands = PracticeHands.Left, PlayOtherHand = false });
        for (var i = 0; i < 20; i++)
        {
            engine.Update(0.5, output);
            foreach (var pitch in engine.Expected) engine.Press(pitch);
        }
        Assert.Empty(output);
        Assert.True(engine.IsFinished);
        Assert.Equal(NoteStatus.Silent, engine.StatusOf(TwoBars.Notes.ToList().FindIndex(n => n.Pitch == P("C4"))));
    }

    [Fact]
    public void Finishes_after_the_last_note_and_silences_the_piano()
    {
        var (engine, output) = Start(new PlayAlongSetup { Hands = PracticeHands.Right });
        for (var i = 0; i < 40 && !engine.IsFinished; i++)
        {
            engine.Update(0.5, output);
            foreach (var pitch in engine.Expected) engine.Press(pitch);
        }
        Assert.True(engine.IsFinished);
        Assert.Equal(5, engine.Result.Chords);
        Assert.Equal(5, engine.Result.CleanChords);
        Assert.Equal(1, engine.Result.Passes);

        // Every note the piano started was stopped again.
        var on = output.Where(a => a.IsOn).GroupBy(a => a.Pitch).ToDictionary(g => g.Key, g => g.Count());
        var off = output.Where(a => !a.IsOn).GroupBy(a => a.Pitch).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(on, off);
    }

    [Fact]
    public void Listen_mode_plays_every_note_without_waiting()
    {
        var (engine, output) = Start(new PlayAlongSetup { Hands = PracticeHands.None });
        for (var i = 0; i < 40 && !engine.IsFinished; i++) engine.Update(0.25, output);

        Assert.True(engine.IsFinished);
        Assert.Equal(TwoBars.Notes.Count, output.Count(a => a.IsOn));
        Assert.Equal(TwoBars.Notes.Count, output.Count(a => !a.IsOn));
    }

    [Fact]
    public void A_bar_range_only_includes_its_own_notes()
    {
        var (engine, output) = Start(new PlayAlongSetup { FromBar = 2, LeadInSeconds = 1 });
        Assert.Equal(4, engine.RangeStart);
        Assert.Equal(8, engine.RangeEnd);
        Assert.Equal(3, engine.Position);
        Assert.Equal(NoteStatus.OutOfRange, engine.StatusOf(0));

        engine.Update(5, output);
        Assert.Equal(4, engine.Position);
        Assert.Equal([P("C3"), P("G4")], engine.Expected.Order());
    }

    [Fact]
    public void Looping_goes_back_to_the_start_of_the_range()
    {
        var (engine, output) = Start(new PlayAlongSetup { Hands = PracticeHands.Right, FromBar = 2, ToBar = 2, Loop = true });
        engine.Update(10, output);
        engine.Press(P("G4"));
        engine.Update(3.99, output);
        Assert.False(engine.IsFinished);
        engine.Update(0.02, output);

        Assert.False(engine.IsFinished);
        Assert.Equal(1, engine.Result.Passes);
        Assert.True(engine.Position < 4);
        Assert.Equal([P("G4")], engine.Expected);
    }

    [Fact]
    public void Tempo_scales_how_fast_the_song_moves()
    {
        var (engine, output) = Start(new PlayAlongSetup { Mode = PlayAlongMode.Flow, Tempo = 0.5, LeadInSeconds = 0 });
        engine.Update(2, output);
        Assert.Equal(1, engine.Position);
        engine.Tempo = 1;
        engine.Update(1, output);
        Assert.Equal(2, engine.Position);
    }

    [Fact]
    public void Flow_mode_scores_hits_within_the_window_and_misses_the_rest()
    {
        var (engine, output) = Start(new PlayAlongSetup { Mode = PlayAlongMode.Flow, Hands = PracticeHands.Right, LeadInSeconds = 0 });
        engine.Update(0.1, output);
        Assert.Equal(PressResult.Correct, engine.Press(P("C4"))); // 100 ms late
        Assert.Equal(PressResult.Wrong, engine.Press(P("E4")));   // 1.9 s early

        engine.Update(1.5, output); // D4 at 1.0 is now 0.6 s ago
        Assert.Equal(PressResult.Wrong, engine.Press(P("D4")));

        var result = engine.Result;
        Assert.Equal(1, result.NotesHit);
        Assert.Equal(1, result.NotesMissed);
        Assert.Equal(2, result.WrongNotes);
        Assert.Equal(100, result.MeanAbsTimingMs, 3);
        Assert.False(engine.IsWaiting);
    }

    [Fact]
    public void Restart_resets_progress_and_silences_the_piano()
    {
        var (engine, output) = Start(new PlayAlongSetup { Hands = PracticeHands.Right, LeadInSeconds = 1 });
        engine.Update(5, output);
        engine.Press(P("C4"));
        engine.Update(0.01, output);
        output.Clear();

        engine.Restart(output);
        Assert.Equal([new NoteAction(P("C3"), 0, false, Hand.Left)], output);
        Assert.Equal(-1, engine.Position);
        Assert.Equal(NoteStatus.Upcoming, engine.StatusOf(TwoBars.Notes.ToList().FindIndex(n => n.Pitch == P("C4"))));
    }
}
