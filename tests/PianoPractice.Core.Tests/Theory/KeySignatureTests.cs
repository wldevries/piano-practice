using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Theory;

public class KeySignatureTests
{
    [Theory]
    [InlineData(0, 66, "F#4")]   // C major: chromatic notes as sharps
    [InlineData(1, 66, "F#4")]   // G major: F# is diatonic
    [InlineData(-1, 70, "Bb4")]  // F major
    [InlineData(-1, 66, "Gb4")]  // F major: chromatic notes as flats
    [InlineData(-6, 59, "Cb4")]  // Gb major: Cb sounds as B3 but is written with octave 4
    [InlineData(7, 60, "B#3")]   // C# major: B# sounds as C4
    [InlineData(6, 65, "E#4")]   // F# major
    [InlineData(2, 65, "F4")]    // D major: F natural is chromatic
    public void Spell(int fifths, int midi, string expected)
    {
        var spelled = new KeySignature(fifths).Spell(new Pitch(midi));
        Assert.Equal(expected, spelled.ToString());
        Assert.Equal(midi, spelled.Pitch.MidiNumber);
    }

    [Fact]
    public void Every_key_spells_every_piano_key_back_to_the_same_pitch()
    {
        foreach (var key in KeySignature.All)
        {
            for (var midi = Pitch.PianoLowest; midi <= Pitch.PianoHighest; midi++)
            {
                Assert.Equal(midi, key.Spell(new Pitch(midi)).Pitch.MidiNumber);
            }
        }
    }

    [Fact]
    public void Displayed_accidentals_are_relative_to_the_key()
    {
        var g = new KeySignature(1);
        Assert.Null(g.DisplayedAccidental(new SpelledPitch(Letter.F, 1, 4)));  // F# is in the key
        Assert.Equal(0, g.DisplayedAccidental(new SpelledPitch(Letter.F, 0, 4))); // F needs a natural
        Assert.Equal(1, g.DisplayedAccidental(new SpelledPitch(Letter.C, 1, 4))); // C# needs a sharp
        Assert.Null(KeySignature.CMajor.DisplayedAccidental(new SpelledPitch(Letter.C, 0, 4)));
    }

    [Theory]
    [InlineData(0, "C major / A minor")]
    [InlineData(2, "D major / B minor (2♯)")]
    [InlineData(-3, "Eb major / C minor (3♭)")]
    public void Display_name(int fifths, string expected) => Assert.Equal(expected, new KeySignature(fifths).ToString());

    [Fact]
    public void Accidental_letters_follow_order_of_sharps_and_flats()
    {
        Assert.Equal([Letter.F, Letter.C, Letter.G], new KeySignature(3).AccidentalLetters);
        Assert.Equal([Letter.B, Letter.E], new KeySignature(-2).AccidentalLetters);
    }
}
