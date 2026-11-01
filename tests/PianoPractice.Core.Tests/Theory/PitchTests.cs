using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Theory;

public class PitchTests
{
    [Theory]
    [InlineData(60, "C4")]
    [InlineData(21, "A0")]
    [InlineData(108, "C8")]
    [InlineData(61, "C#4")]
    [InlineData(59, "B3")]
    public void ToString_uses_scientific_pitch_notation(int midi, string expected)
        => Assert.Equal(expected, new Pitch(midi).ToString());

    [Theory]
    [InlineData(61, true)]
    [InlineData(60, false)]
    [InlineData(70, true)]
    [InlineData(64, false)]
    public void IsBlackKey(int midi, bool expected)
        => Assert.Equal(expected, new Pitch(midi).IsBlackKey);

    [Fact]
    public void Prefer_flats_spells_black_keys_with_flats()
        => Assert.Equal("Eb4", SpelledPitch.FromPitch(new Pitch(63), preferFlats: true).ToString());

    [Fact]
    public void Octave_follows_letter_for_enharmonic_spellings()
    {
        // B#3 and Cb4 cross the octave boundary.
        Assert.Equal(60, new SpelledPitch(Letter.B, 1, 3).Pitch.MidiNumber);
        Assert.Equal(59, new SpelledPitch(Letter.C, -1, 4).Pitch.MidiNumber);
    }

    [Fact]
    public void Same_letter_different_accidental_shares_diatonic_index()
        => Assert.Equal(
            new SpelledPitch(Letter.F, 0, 4).DiatonicIndex,
            new SpelledPitch(Letter.F, 1, 4).DiatonicIndex);

    [Fact]
    public void Every_piano_key_round_trips_through_spelling()
    {
        for (var midi = Pitch.PianoLowest; midi <= Pitch.PianoHighest; midi++)
        {
            Assert.Equal(midi, SpelledPitch.FromPitch(new Pitch(midi)).Pitch.MidiNumber);
            Assert.Equal(midi, SpelledPitch.FromPitch(new Pitch(midi), preferFlats: true).Pitch.MidiNumber);
        }
    }
}
