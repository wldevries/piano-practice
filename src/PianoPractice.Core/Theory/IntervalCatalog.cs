namespace PianoPractice.Core.Theory;

/// <summary>A simple interval by size, with the classic "reference song" memory aids used in ear training.</summary>
public sealed record IntervalInfo(int Semitones, string Short, string Name, string AscendingSong, string DescendingSong);

public static class IntervalCatalog
{
    public static IReadOnlyList<IntervalInfo> All { get; } =
    [
        new(1, "m2", "Minor 2nd", "Jaws", "Für Elise"),
        new(2, "M2", "Major 2nd", "Happy Birthday", "Mary Had a Little Lamb"),
        new(3, "m3", "Minor 3rd", "Greensleeves", "Hey Jude"),
        new(4, "M3", "Major 3rd", "When the Saints Go Marching In", "Swing Low, Sweet Chariot"),
        new(5, "P4", "Perfect 4th", "Here Comes the Bride", "O Come, All Ye Faithful"),
        new(6, "TT", "Tritone", "The Simpsons", "Black Sabbath"),
        new(7, "P5", "Perfect 5th", "Twinkle Twinkle Little Star", "The Flintstones"),
        new(8, "m6", "Minor 6th", "The Entertainer", "Love Story (Where Do I Begin)"),
        new(9, "M6", "Major 6th", "My Bonnie Lies over the Ocean", "Nobody Knows the Trouble I've Seen"),
        new(10, "m7", "Minor 7th", "Star Trek (original theme)", "An American in Paris"),
        new(11, "M7", "Major 7th", "Take On Me", "I Love You (Cole Porter)"),
        new(12, "P8", "Octave", "Somewhere Over the Rainbow", "Willow Weep for Me"),
    ];

    public static IntervalInfo Get(int semitones) =>
        All.FirstOrDefault(i => i.Semitones == semitones)
        ?? throw new ArgumentOutOfRangeException(nameof(semitones), "Only simple intervals (1..12 semitones) are supported.");
}
