namespace PianoPractice.Core.Notation;

public enum NoteValueKind { Whole, Half, Quarter, Eighth, Sixteenth, ThirtySecond }

/// <summary>A written note or rest length, e.g. a dotted quarter. Lengths are in quarter notes.</summary>
public readonly record struct NoteValue(NoteValueKind Kind, int Dots = 0)
{
    private static readonly NoteValue[] NoteCandidates =
    [
        new(NoteValueKind.Whole, 1), new(NoteValueKind.Whole), new(NoteValueKind.Half, 1), new(NoteValueKind.Half),
        new(NoteValueKind.Quarter, 1), new(NoteValueKind.Quarter), new(NoteValueKind.Eighth, 1), new(NoteValueKind.Eighth),
        new(NoteValueKind.Sixteenth, 1), new(NoteValueKind.Sixteenth), new(NoteValueKind.ThirtySecond),
    ];

    public double Quarters => Kind switch
    {
        NoteValueKind.Whole => 4,
        NoteValueKind.Half => 2,
        NoteValueKind.Quarter => 1,
        NoteValueKind.Eighth => 0.5,
        NoteValueKind.Sixteenth => 0.25,
        _ => 0.125,
    } * (Dots == 1 ? 1.5 : 1);

    /// <summary>Flags on a single note, or beams in a beamed group.</summary>
    public int Flags => Kind switch
    {
        NoteValueKind.Eighth => 1,
        NoteValueKind.Sixteenth => 2,
        NoteValueKind.ThirtySecond => 3,
        _ => 0,
    };

    public bool HasStem => Kind != NoteValueKind.Whole;

    /// <summary>The written value closest to a length (in ratio, so 0.9 quarters is a quarter, not an eighth).</summary>
    public static NoteValue Nearest(double quarters)
    {
        if (quarters <= 0) return new(NoteValueKind.ThirtySecond);
        return NoteCandidates.MinBy(c => Math.Abs(Math.Log(quarters / c.Quarters)));
    }

    /// <summary>The longest plain (undotted) value that fits; for filling gaps with rests.</summary>
    /// <param name="alignedAt">If given, only values that start on a multiple of their own length from here (bar position in quarters).</param>
    public static NoteValue LargestFitting(double quarters, double? alignedAt = null) =>
        NoteCandidates
            .Where(c => c.Dots == 0 && c.Quarters <= quarters + 1e-6)
            .Where(c => alignedAt is not { } at || Math.Abs(at / c.Quarters - Math.Round(at / c.Quarters)) < 1e-6)
            .DefaultIfEmpty(new(NoteValueKind.ThirtySecond))
            .First();
}
