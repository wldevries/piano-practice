using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Notation;

/// <summary>A note placed in a chord on one staff.</summary>
/// <param name="InputIndex">Index of the note in the list passed to <see cref="ChordLayout.Compute"/>.</param>
/// <param name="Step">Staff step (see <see cref="StaffPosition"/>).</param>
/// <param name="Displaced">Notehead sits right of the stem side, to clear a neighbour a second away.</param>
/// <param name="Accidental">Sign to print (-2..2, 0 = natural), or null.</param>
/// <param name="AccidentalColumn">0 = closest to the notehead; higher = further left.</param>
public sealed record PlacedNote(int InputIndex, int Step, bool Displaced, int? Accidental, int AccidentalColumn);

public sealed record ChordLayoutResult(
    IReadOnlyList<PlacedNote> Notes,
    IReadOnlyList<int> LedgerLines,
    bool HasDisplacedNotes,
    int AccidentalColumnCount);

/// <summary>
/// Lays out a stemless chord (whole notes) on one staff: seconds side by side,
/// accidentals stacked into columns so they don't collide, ledger lines.
/// </summary>
public static class ChordLayout
{
    /// <summary>Accidentals in the same column need this many steps between them (≈ glyph height).</summary>
    public const int AccidentalClearance = 6;

    /// <param name="displayedAccidental">Which sign to print for a note; defaults to the key signature alone (no bar context).</param>
    public static ChordLayoutResult Compute(IReadOnlyList<SpelledPitch> notes, Clef clef, KeySignature key,
        Func<SpelledPitch, int?>? displayedAccidental = null)
    {
        displayedAccidental ??= key.DisplayedAccidental;
        var ordered = notes
            .Select((pitch, index) => (Index: index, Pitch: pitch, Step: StaffPosition.Step(pitch, clef)))
            .OrderBy(n => n.Step)
            .ThenBy(n => n.Pitch.Accidental)
            .ToList();

        // Seconds (and same-step clashes like F/F#): bottom-up, a note next to an
        // undisplaced note moves to the other side.
        var displaced = new bool[ordered.Count];
        for (var i = 1; i < ordered.Count; i++)
        {
            displaced[i] = ordered[i].Step - ordered[i - 1].Step <= 1 && !displaced[i - 1];
        }

        // Accidentals: top-down, each takes the first column with enough vertical room.
        var columns = new List<List<int>>();
        var columnOf = new int[ordered.Count];
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (displayedAccidental(ordered[i].Pitch) is null) continue;
            var step = ordered[i].Step;
            var column = columns.FindIndex(c => c.All(s => Math.Abs(s - step) >= AccidentalClearance));
            if (column < 0)
            {
                columns.Add([]);
                column = columns.Count - 1;
            }
            columns[column].Add(step);
            columnOf[i] = column;
        }

        var placed = ordered
            .Select((n, i) => new PlacedNote(n.Index, n.Step, displaced[i], displayedAccidental(n.Pitch), columnOf[i]))
            .ToList();

        var ledgers = ordered.Count == 0
            ? []
            : StaffPosition.LedgerLines(ordered[0].Step).Concat(StaffPosition.LedgerLines(ordered[^1].Step)).Distinct().ToList();

        return new ChordLayoutResult(placed, ledgers, displaced.Any(d => d), columns.Count);
    }
}
