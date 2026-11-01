using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.Theory;

public class NoteNamesTests
{
    [Theory]
    [InlineData(0, "P1")]
    [InlineData(4, "M3")]
    [InlineData(6, "TT")]
    [InlineData(7, "P5")]
    [InlineData(-7, "P5")]
    [InlineData(12, "P8")]
    [InlineData(16, "M3 +8va")]
    [InlineData(31, "P5 +2×8va")]
    public void Interval(int semitones, string expected) => Assert.Equal(expected, NoteNames.Interval(semitones));

    [Theory]
    [InlineData(63, "Eb4")]
    [InlineData(66, "F#4")]
    [InlineData(21, "A0")]
    public void WithOctave(int midi, string expected) => Assert.Equal(expected, NoteNames.WithOctave(new Pitch(midi)));
}
