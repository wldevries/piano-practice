using PianoPractice.Core.Notation;
using PianoPractice.Core.PlayAlong;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Notation;

public class ScoreBuilderTests
{
    private static NoteValue V(NoteValueKind kind, int dots = 0) => new(kind, dots);

    private static List<ScoreChord> Bar(Score score, Hand hand, int bar) =>
        score.Chords.Where(c => c.Hand == hand && c.Bar == bar).ToList();

    [Theory]
    [InlineData(1.0, NoteValueKind.Quarter, 0)]
    [InlineData(0.9, NoteValueKind.Quarter, 0)]
    [InlineData(1.5, NoteValueKind.Quarter, 1)]
    [InlineData(3.0, NoteValueKind.Half, 1)]
    [InlineData(0.25, NoteValueKind.Sixteenth, 0)]
    [InlineData(8.0, NoteValueKind.Whole, 1)]
    public void Nearest_note_value(double quarters, NoteValueKind kind, int dots)
    {
        Assert.Equal(V(kind, dots), NoteValue.Nearest(quarters));
    }

    [Fact]
    public void Rhythms_of_ode_to_joy()
    {
        var score = ScoreBuilder.Build(BuiltInSongs.Load("03-ode-to-joy.song"));

        // Bar 4: E4/q. D4/8 D4/h
        Assert.Equal([V(NoteValueKind.Quarter, 1), V(NoteValueKind.Eighth), V(NoteValueKind.Half)],
            Bar(score, Hand.Right, 3).Select(c => c.Value));
        Assert.All(Bar(score, Hand.Right, 3), c => Assert.Equal(-1, c.Beam)); // a lone eighth gets a flag

        // Bar 10: D4/q E4/8 F4 E4/q C4 — the two eighths share a beam.
        var bar10 = Bar(score, Hand.Right, 9);
        Assert.Equal(-1, bar10[0].Beam);
        Assert.True(bar10[1].Beam >= 0);
        Assert.Equal(bar10[1].Beam, bar10[2].Beam);
        Assert.Equal([1, 2], score.Beams[bar10[1].Beam].Chords.Select(i => bar10.IndexOf(score.Chords[i])));

        // Left hand: whole notes on the bass staff, no rests where it plays.
        Assert.Equal([V(NoteValueKind.Whole)], Bar(score, Hand.Left, 0).Select(c => c.Value));
        Assert.DoesNotContain(score.Rests, r => r.Hand == Hand.Left && r.Start < 2);
    }

    [Fact]
    public void Fur_elise_pickup_beams_and_accidentals()
    {
        var score = ScoreBuilder.Build(BuiltInSongs.Load("10-fur-elise.song"));

        // The pickup bar is two sixteenths long, beamed together.
        var pickup = Bar(score, Hand.Right, 0);
        Assert.Equal(2, pickup.Count);
        Assert.All(pickup, c => Assert.Equal(V(NoteValueKind.Sixteenth), c.Value));
        Assert.True(pickup[0].Beam >= 0 && pickup[0].Beam == pickup[1].Beam);

        // Bar 2 (index 1): E5 D#5 E5 B4 D5 C5 — one beam of six; D#5 gets a sharp, the later D5 a natural.
        var bar = Bar(score, Hand.Right, 1);
        Assert.Equal(6, bar.Count);
        Assert.Single(bar.Select(c => c.Beam).Distinct());
        Assert.Equal(1, bar[1].Layout.Notes[0].Accidental);
        Assert.Null(bar[2].Layout.Notes[0].Accidental);
        Assert.Equal(0, bar[4].Layout.Notes[0].Accidental);

        // Left hand rests through the first full bar.
        Assert.Contains(score.Rests, r => r.Hand == Hand.Left && r.WholeBar && Math.Abs(r.Start - score.Chords.First(c => c.Bar == 1).Start) < 1e-6);
    }

    [Fact]
    public void Rests_fill_the_gaps_in_a_bar()
    {
        var song = SongText.Parse("tempo: 60\nRH: C4/q r/q r/h | C4/h r/8 D4/8 r/q |");
        var score = ScoreBuilder.Build(song);
        var rests = score.Rests.Where(r => r.Hand == Hand.Right).ToList();

        Assert.Equal([V(NoteValueKind.Quarter), V(NoteValueKind.Half), V(NoteValueKind.Eighth), V(NoteValueKind.Quarter)], rests.Select(r => r.Value));
        Assert.Equal([1.0, 2.0, 6.0, 7.0], rests.Select(r => r.Start));
    }

    [Fact]
    public void Quantizes_sloppy_timing_to_sixteenths()
    {
        // Quarter notes played a little early/late and short, at 60 bpm.
        double[] starts = [0.02, 0.97, 2.04, 2.99];
        var notes = starts.Select(s => new SongNote(Pitch.MiddleC, s, 0.85, Hand.Right));
        var song = new Song("sloppy", notes, [0], 60);
        var score = ScoreBuilder.Build(song);

        Assert.Equal([0.0, 1.0, 2.0, 3.0], score.Chords.Select(c => c.StartQuarters));
        Assert.All(score.Chords, c => Assert.Equal(V(NoteValueKind.Quarter), c.Value));
    }

    [Fact]
    public void High_passages_are_written_an_octave_lower_under_8va()
    {
        // Like the Bubble Bobble intro: arpeggios up to C7, then a normal melody.
        var score = ScoreBuilder.Build(SongText.Parse("RH: C7/16 G6 D#6 C6 C7 G6 D#6 C6 C7 G6 D#6 C6 C7 G6 D#6 C6 | C5/w |"));
        var high = score.Chords.Take(16).ToList();

        Assert.All(high, c => Assert.True(c.Ottava));
        Assert.Equal(StaffPosition.Step(new SpelledPitch(Letter.C, 0, 6), Clef.Treble), high[0].HighestStep); // C7 written as C6
        Assert.False(score.Chords[^1].Ottava);
        Assert.DoesNotContain(ScoreBuilder.Build(BuiltInSongs.Load("10-fur-elise.song")).Chords, c => c.Ottava);
    }

    [Fact]
    public void Stems_point_away_from_the_middle_of_the_staff()
    {
        var score = ScoreBuilder.Build(SongText.Parse("RH: C4/h A5 |"));
        Assert.True(score.Chords[0].StemUp);   // C4 below the treble staff
        Assert.False(score.Chords[1].StemUp);  // A5 at the top
    }

    [Theory]
    [InlineData("10-fur-elise.song", 0)]
    [InlineData("09-minuet-in-g.song", 1)]
    [InlineData("03-ode-to-joy.song", 0)]
    public void Key_guess_matches_the_written_key(string id, int fifths)
    {
        var song = BuiltInSongs.Load(id);
        Assert.Equal(fifths, KeyGuess.FromNotes(song.Notes.Select(n => (n.Pitch, n.Duration))).Fifths);
    }

    [Fact]
    public void Key_guess_finds_flat_keys()
    {
        // B-flat major scale with E-flat and B-flat.
        int[] scale = [58, 60, 62, 63, 65, 67, 69, 70];
        Assert.Equal(-2, KeyGuess.FromNotes(scale.Select(m => (new Pitch(m), 1.0))).Fifths);
    }
}
