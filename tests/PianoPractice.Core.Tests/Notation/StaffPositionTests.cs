using PianoPractice.Core.Notation;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Notation;

public class StaffPositionTests
{
    [Theory]
    [InlineData(Letter.E, 4, Clef.Treble, 0)]   // bottom line
    [InlineData(Letter.F, 5, Clef.Treble, 8)]   // top line
    [InlineData(Letter.B, 4, Clef.Treble, 4)]   // middle line
    [InlineData(Letter.C, 4, Clef.Treble, -2)]  // first ledger line below
    [InlineData(Letter.G, 2, Clef.Bass, 0)]
    [InlineData(Letter.A, 3, Clef.Bass, 8)]
    [InlineData(Letter.D, 3, Clef.Bass, 4)]
    [InlineData(Letter.C, 4, Clef.Bass, 10)]    // first ledger line above
    [InlineData(Letter.A, 0, Clef.Bass, -13)]   // lowest piano key
    [InlineData(Letter.C, 8, Clef.Treble, 26)]  // highest piano key
    public void Step(Letter letter, int octave, Clef clef, int expected)
        => Assert.Equal(expected, StaffPosition.Step(new SpelledPitch(letter, 0, octave), clef));

    [Fact]
    public void Accidentals_do_not_move_the_note()
        => Assert.Equal(
            StaffPosition.Step(new SpelledPitch(Letter.G, 0, 4), Clef.Treble),
            StaffPosition.Step(new SpelledPitch(Letter.G, 1, 4), Clef.Treble));

    [Fact]
    public void Every_piano_key_lands_on_its_letter_position_on_the_grand_staff()
    {
        var key = KeySignature.CMajor;
        int? previousStep = null;
        var previousClef = Clef.Bass;
        for (var midi = Pitch.PianoLowest; midi <= Pitch.PianoHighest; midi++)
        {
            var pitch = new Pitch(midi);
            var spelled = key.Spell(pitch);
            var clef = StaffPosition.ClefFor(pitch);
            var step = StaffPosition.Step(spelled, clef);

            // Within a staff, going up a semitone moves up 0 or 1 step (sharps share the letter's step).
            if (previousStep is not null && clef == previousClef)
            {
                Assert.InRange(step - previousStep.Value, 0, 1);
            }
            previousStep = step;
            previousClef = clef;
        }
    }

    [Theory]
    [InlineData(4, new int[0])]
    [InlineData(-2, new[] { -2 })]
    [InlineData(-3, new[] { -2 })]          // space below the first ledger line
    [InlineData(-5, new[] { -2, -4 })]
    [InlineData(10, new[] { 10 })]
    [InlineData(13, new[] { 10, 12 })]
    public void Ledger_lines(int step, int[] expected) => Assert.Equal(expected, StaffPosition.LedgerLines(step));

    [Fact]
    public void Key_signature_positions()
    {
        // D major: F# on the top line, C# in the third space (treble); two steps lower in the bass.
        Assert.Equal([8, 5], StaffPosition.KeySignatureSteps(new KeySignature(2), Clef.Treble));
        Assert.Equal([6, 3], StaffPosition.KeySignatureSteps(new KeySignature(2), Clef.Bass));
        // Bb major: Bb on the middle line, Eb in the top space.
        Assert.Equal([4, 7], StaffPosition.KeySignatureSteps(new KeySignature(-2), Clef.Treble));
    }
}
