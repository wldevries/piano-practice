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

    // Conventional spelling of each interval above C: minor intervals use flats, the tritone F#.
    private static readonly (Letter Letter, int Accidental)[] AboveC =
    [
        (Letter.D, -1), (Letter.D, 0), (Letter.E, -1), (Letter.E, 0), (Letter.F, 0), (Letter.F, 1),
        (Letter.G, 0), (Letter.A, -1), (Letter.A, 0), (Letter.B, -1), (Letter.B, 0), (Letter.C, 0),
    ];

    /// <summary>The upper note of an interval starting on middle C, spelled as in a textbook (C→E♭ for a minor 3rd).</summary>
    public static SpelledPitch AboveMiddleC(int semitones)
    {
        var (letter, accidental) = AboveC[Get(semitones).Semitones - 1];
        return new SpelledPitch(letter, accidental, semitones == 12 ? 5 : 4);
    }

    /// <summary>One-line explanation for beginners, e.g. "7 half steps · C → G · 5 letter names (C D E F G)".</summary>
    public static string Explain(int semitones)
    {
        var top = AboveMiddleC(semitones);
        var letters = Enumerable.Range(0, top.DiatonicIndex - new SpelledPitch(Letter.C, 0, 4).DiatonicIndex + 1)
            .Select(i => ((Letter)(i % 7)).ToString());
        var topName = top.Letter + top.AccidentalSymbol.Replace("b", "♭").Replace("#", "♯");
        return $"{semitones} half step{(semitones == 1 ? "" : "s")}  ·  C → {topName}  ·  letters {string.Join(" ", letters)}";
    }

    public static IntervalInfo Get(int semitones) =>
        All.FirstOrDefault(i => i.Semitones == semitones)
        ?? throw new ArgumentOutOfRangeException(nameof(semitones), "Only simple intervals (1..12 semitones) are supported.");
}
