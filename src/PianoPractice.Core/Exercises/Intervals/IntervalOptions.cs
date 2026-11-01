using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Exercises.Intervals;

public enum IntervalDirection { Ascending, Descending, Harmonic }

public enum DirectionChoice { Ascending, Descending, Harmonic, Mixed }

public enum IntervalAnswerMode
{
    /// <summary>The first note is given; find the second on the piano.</summary>
    PlayBack,
    /// <summary>Pick the interval's name.</summary>
    Name,
}

public sealed record IntervalOptions
{
    /// <summary>Interval sizes to practise, in semitones (1..12).</summary>
    public IReadOnlyList<int> Intervals { get; init; } = [7, 12];

    public DirectionChoice Direction { get; init; } = DirectionChoice.Ascending;
    public IntervalAnswerMode AnswerMode { get; init; } = IntervalAnswerMode.PlayBack;

    /// <summary>Range for the lower note of each interval (MIDI). Default C3..C5.</summary>
    public int LowestNote { get; init; } = 48;
    public int HighestNote { get; init; } = 72;

    public int QuestionsPerSession { get; init; } = 20;

    /// <summary>A gradual path from easy to hard, the usual order in ear training.</summary>
    public static IReadOnlyList<(string Name, int[] Intervals)> Stages { get; } =
    [
        ("1 · Fifth and octave", [7, 12]),
        ("2 · + Major 3rd, 4th", [4, 5, 7, 12]),
        ("3 · + Minor 3rd, major 6th", [3, 4, 5, 7, 9, 12]),
        ("4 · + 2nds, minor 6th, minor 7th", [1, 2, 3, 4, 5, 7, 8, 9, 10, 12]),
        ("5 · Everything", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]),
    ];

    public IReadOnlyList<IntervalDirection> Directions => Direction switch
    {
        DirectionChoice.Ascending => [IntervalDirection.Ascending],
        DirectionChoice.Descending => [IntervalDirection.Descending],
        DirectionChoice.Harmonic => [IntervalDirection.Harmonic],
        _ => [IntervalDirection.Ascending, IntervalDirection.Descending, IntervalDirection.Harmonic],
    };

    public string Describe()
    {
        var names = string.Join(" ", Intervals.Order().Select(s => IntervalCatalog.Get(s).Short));
        var direction = Direction.ToString().ToLowerInvariant();
        var mode = AnswerMode == IntervalAnswerMode.PlayBack ? "play back" : "name it";
        return $"{names}, {direction}, {mode}";
    }
}
