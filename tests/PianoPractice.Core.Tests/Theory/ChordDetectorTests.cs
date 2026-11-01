using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Theory;

public class ChordDetectorTests
{
    private static string? Name(params int[] midi) => ChordDetector.Detect(midi.Select(m => new Pitch(m)))?.Name;

    [Theory]
    [InlineData("C", 60, 64, 67)]
    [InlineData("Am", 57, 60, 64)]
    [InlineData("G7", 55, 59, 62, 65)]
    [InlineData("Cmaj7", 60, 64, 67, 71)]
    [InlineData("Dm7", 62, 65, 69, 72)]
    [InlineData("Bdim", 59, 62, 65)]
    [InlineData("Caug", 60, 64, 68)]
    [InlineData("Dsus4", 62, 67, 69)]
    [InlineData("Bm7b5", 59, 62, 65, 69)]
    [InlineData("Eb", 63, 67, 70)]
    [InlineData("F#m", 66, 69, 73)]
    [InlineData("C5", 48, 55)]
    public void Root_position_chords(string expected, params int[] midi) => Assert.Equal(expected, Name(midi));

    [Theory]
    [InlineData("C/E", 64, 67, 72)]
    [InlineData("C/G", 55, 60, 64)]
    [InlineData("G7/B", 59, 62, 65, 67)]
    [InlineData("Am7/G", 55, 57, 60, 64)]
    public void Inversions_show_bass(string expected, params int[] midi) => Assert.Equal(expected, Name(midi));

    [Fact]
    public void Octave_doublings_are_ignored() => Assert.Equal("C", Name(48, 60, 64, 67, 72));

    [Fact]
    public void Bass_decides_ambiguous_sets()
    {
        // Same notes, C6 vs Am7: the bass note picks the reading.
        Assert.Equal("C6", Name(60, 64, 67, 69));
        Assert.Equal("Am7", Name(57, 60, 64, 67));
    }

    [Fact]
    public void Seventh_without_fifth() => Assert.Equal("G7", Name(43, 59, 65));

    [Theory]
    [InlineData(60)]
    [InlineData(60, 72)]
    [InlineData(60, 64)]       // a bare third is an interval, not a chord
    [InlineData(60, 61, 62)]   // cluster
    public void Not_a_chord(params int[] midi) => Assert.Null(Name(midi));
}
