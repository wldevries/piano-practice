using PianoPractice.Core.Notation;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Notation;

public class ChordLayoutTests
{
    private static SpelledPitch P(Letter letter, int octave, int accidental = 0) => new(letter, accidental, octave);

    private static ChordLayoutResult Layout(KeySignature key, params SpelledPitch[] notes)
        => ChordLayout.Compute(notes, Clef.Treble, key);

    [Fact]
    public void Triad_in_thirds_has_no_displaced_notes()
    {
        var result = Layout(KeySignature.CMajor, P(Letter.C, 4), P(Letter.E, 4), P(Letter.G, 4));
        Assert.False(result.HasDisplacedNotes);
        Assert.Equal([-2], result.LedgerLines);
    }

    [Fact]
    public void Second_puts_upper_note_on_the_other_side()
    {
        var result = Layout(KeySignature.CMajor, P(Letter.D, 5), P(Letter.C, 5));
        var c = result.Notes.Single(n => n.InputIndex == 1);
        var d = result.Notes.Single(n => n.InputIndex == 0);
        Assert.False(c.Displaced);
        Assert.True(d.Displaced);
    }

    [Fact]
    public void Cluster_alternates_sides()
    {
        var result = Layout(KeySignature.CMajor, P(Letter.C, 5), P(Letter.D, 5), P(Letter.E, 5));
        Assert.Equal([false, true, false], result.Notes.Select(n => n.Displaced));
    }

    [Fact]
    public void Accidentals_close_together_use_separate_columns()
    {
        // F#4 and A#4 are 2 steps apart. Top-down: A#4 takes column 0, F#4 would clash → column 1.
        var result = Layout(KeySignature.CMajor, P(Letter.F, 4, 1), P(Letter.A, 4, 1));
        Assert.Equal(2, result.AccidentalColumnCount);
        Assert.Equal(0, result.Notes.Single(n => n.InputIndex == 1).AccidentalColumn);
        Assert.Equal(1, result.Notes.Single(n => n.InputIndex == 0).AccidentalColumn);
    }

    [Fact]
    public void Accidentals_an_octave_apart_share_a_column()
    {
        var result = Layout(KeySignature.CMajor, P(Letter.F, 4, 1), P(Letter.F, 5, 1));
        Assert.Equal(1, result.AccidentalColumnCount);
    }

    [Fact]
    public void Key_signature_suppresses_accidentals_and_adds_naturals()
    {
        var d = new KeySignature(2); // F#, C#
        var result = Layout(d, P(Letter.F, 4, 1), P(Letter.C, 5, 0));
        Assert.Null(result.Notes.Single(n => n.InputIndex == 0).Accidental);
        Assert.Equal(0, result.Notes.Single(n => n.InputIndex == 1).Accidental); // C natural
    }

    [Fact]
    public void Ledger_lines_cover_both_extremes()
    {
        var result = Layout(KeySignature.CMajor, P(Letter.A, 3), P(Letter.C, 6));
        Assert.Equal([-2, -4, 10, 12], result.LedgerLines);
    }

    [Fact]
    public void Empty_chord() => Assert.Empty(Layout(KeySignature.CMajor).Notes);
}
