using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Notation;

public enum Clef { Treble, Bass }

/// <summary>
/// Vertical positions on a five-line staff, in steps (half staff spaces):
/// 0 = bottom line, 1 = first space, ..., 8 = top line. Negative = below the staff.
/// Even steps are lines (or ledger lines), odd steps are spaces.
/// </summary>
public static class StaffPosition
{
    public const int BottomLine = 0;
    public const int MiddleLine = 4;
    public const int TopLine = 8;

    // Diatonic index of the note on each clef's bottom line: E4 (treble), G2 (bass).
    private static readonly int TrebleBottom = new SpelledPitch(Letter.E, 0, 4).DiatonicIndex;
    private static readonly int BassBottom = new SpelledPitch(Letter.G, 0, 2).DiatonicIndex;

    public static int Step(SpelledPitch pitch, Clef clef) =>
        pitch.DiatonicIndex - (clef == Clef.Treble ? TrebleBottom : BassBottom);

    /// <summary>The note written at a staff step (inverse of <see cref="Step"/>), with the given accidental.</summary>
    public static SpelledPitch PitchAt(int step, Clef clef, int accidental = 0)
    {
        var index = step + (clef == Clef.Treble ? TrebleBottom : BassBottom);
        return new SpelledPitch((Letter)(index % 7), accidental, index / 7);
    }

    /// <summary>Steps of the clef's reference line: the G line for treble, the F line for bass.</summary>
    public static int ClefLine(Clef clef) => clef == Clef.Treble ? 2 : 6;

    /// <summary>Grand staff assignment: notes at or above the split (default middle C) go on the treble staff.</summary>
    public static Clef ClefFor(Pitch pitch, int splitMidi = 60) =>
        pitch.MidiNumber >= splitMidi ? Clef.Treble : Clef.Bass;

    /// <summary>Steps of the ledger lines needed to reach a note (empty when on the staff).</summary>
    public static IEnumerable<int> LedgerLines(int step)
    {
        for (var s = -2; s >= step; s -= 2) yield return s;
        for (var s = TopLine + 2; s <= step; s += 2) yield return s;
    }

    /// <summary>
    /// Where each key-signature accidental goes, in writing order. The bass clef uses
    /// the same pattern two steps lower than the treble.
    /// </summary>
    public static IReadOnlyList<int> KeySignatureSteps(KeySignature key, Clef clef)
    {
        int[] trebleSharps = [8, 5, 9, 6, 3, 7, 4]; // F5 C5 G5 D5 A4 E5 B4
        int[] trebleFlats = [4, 7, 3, 6, 2, 5, 1];  // B4 E5 A4 D5 G4 C5 F4
        var offset = clef == Clef.Treble ? 0 : -2;
        var steps = key.Fifths >= 0 ? trebleSharps[..key.Fifths] : trebleFlats[..(-key.Fifths)];
        return steps.Select(s => s + offset).ToList();
    }
}
