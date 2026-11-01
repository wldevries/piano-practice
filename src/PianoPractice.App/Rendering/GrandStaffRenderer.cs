using Microsoft.Graphics.Canvas;
using PianoPractice.App.Controls;
using PianoPractice.Core.Notation;
using PianoPractice.Core.Theory;
using Windows.UI;

namespace PianoPractice.App.Rendering;

/// <summary>
/// Draws a grand staff (brace, treble + bass clef, key signature) with one stemless chord.
/// Vertical room is reserved for the full piano range: C8 needs 9 spaces above the treble
/// staff, A0 6.5 below the bass staff.
/// </summary>
public sealed class GrandStaffRenderer : IDisposable
{
    private const float SpaceAboveTreble = 10f;
    private const float StaffGap = 5f;       // treble bottom line → bass top line
    private const float SpaceBelowBass = 7.5f;
    private const float KeySignatureAdvance = 1.0f;
    private const float AccidentalColumnWidth = 1.15f;

    /// <summary>Total height in staff spaces.</summary>
    public const float HeightInSpaces = SpaceAboveTreble + 4 + StaffGap + 4 + SpaceBelowBass;

    private readonly SmuflText _glyphs = new();

    public float StaffSpace { get; set; } = 12f;
    public Color Ink { get; set; } = Color.FromArgb(255, 0, 0, 0);
    public Func<KeyHighlightKind, Color> ColorForKind { get; set; } = _ => Color.FromArgb(255, 0, 0, 0);

    /// <summary>Left margin needed for the brace, in DIPs.</summary>
    public float BraceMargin => (Smufl.BraceWidth * (8 + StaffGap) / 4 + 0.6f) * StaffSpace;

    public void Draw(CanvasDrawingSession ds, float left, float top, float width,
        KeySignature key, IReadOnlyList<StaffNote> notes, int splitMidi = 60)
    {
        var s = StaffSpace;
        var fontSize = 4 * s;
        var trebleTop = top + SpaceAboveTreble * s;
        var trebleBottom = trebleTop + 4 * s;
        var bassTop = trebleBottom + StaffGap * s;
        var bassBottom = bassTop + 4 * s;

        float Y(Clef clef, int step) => (clef == Clef.Treble ? trebleBottom : bassBottom) - step * s / 2f;

        // Staves, connecting barline and brace.
        foreach (var staffTop in new[] { trebleTop, bassTop })
        {
            for (var i = 0; i < 5; i++)
            {
                ds.DrawLine(left, staffTop + i * s, left + width, staffTop + i * s, Ink, Smufl.StaffLineThickness * s);
            }
        }
        ds.DrawLine(left, trebleTop, left, bassBottom, Ink, Smufl.ThinBarlineThickness * s);
        var braceSize = bassBottom - trebleTop; // the brace glyph is one em tall
        _glyphs.Draw(ds, Smufl.Brace, left - 0.4f * s - Smufl.BraceWidth * braceSize / 4, bassBottom, braceSize, Ink);

        // Clefs.
        _glyphs.Draw(ds, Smufl.GClef, left + 0.8f * s, Y(Clef.Treble, StaffPosition.ClefLine(Clef.Treble)), fontSize, Ink);
        _glyphs.Draw(ds, Smufl.FClef, left + 0.8f * s, Y(Clef.Bass, StaffPosition.ClefLine(Clef.Bass)), fontSize, Ink);

        // Key signature.
        var keyX = left + 4.2f * s;
        var keyGlyph = key.Fifths >= 0 ? Smufl.AccidentalSharp : Smufl.AccidentalFlat;
        foreach (var clef in new[] { Clef.Treble, Clef.Bass })
        {
            var steps = StaffPosition.KeySignatureSteps(key, clef);
            for (var i = 0; i < steps.Count; i++)
            {
                _glyphs.Draw(ds, keyGlyph, keyX + i * KeySignatureAdvance * s, Y(clef, steps[i]), fontSize, Ink);
            }
        }
        var keyEnd = keyX + Math.Abs(key.Fifths) * KeySignatureAdvance * s;

        // Chord: split across the staves, laid out per staff, drawn at a shared x.
        var byClef = notes
            .GroupBy(n => StaffPosition.ClefFor(n.Pitch.Pitch, splitMidi))
            .ToDictionary(g => g.Key, g => g.ToList());
        var layouts = byClef.ToDictionary(
            kv => kv.Key,
            kv => ChordLayout.Compute(kv.Value.Select(n => n.Pitch).ToList(), kv.Key, key));

        var accidentalColumns = layouts.Values.Select(l => l.AccidentalColumnCount).DefaultIfEmpty(0).Max();
        var noteWidth = Smufl.NoteheadWholeWidth * s;
        var chordX = Math.Max(
            keyEnd + (left + width - keyEnd) / 2 - noteWidth / 2,
            keyEnd + (1.2f + accidentalColumns * AccidentalColumnWidth) * s);

        foreach (var (clef, layout) in layouts)
        {
            var staffNotes = byClef[clef];
            var ledgerLeft = chordX - Smufl.LedgerLineExtension * s;
            var ledgerRight = chordX + noteWidth * (layout.HasDisplacedNotes ? 2 : 1) + Smufl.LedgerLineExtension * s;
            foreach (var step in layout.LedgerLines)
            {
                ds.DrawLine(ledgerLeft, Y(clef, step), ledgerRight, Y(clef, step), Ink, Smufl.LedgerLineThickness * s);
            }

            foreach (var placed in layout.Notes)
            {
                var note = staffNotes[placed.InputIndex];
                var color = note.Kind is { } kind ? ColorForKind(kind) : Ink;
                var y = Y(clef, placed.Step);
                _glyphs.Draw(ds, Smufl.NoteheadWhole, chordX + (placed.Displaced ? noteWidth : 0), y, fontSize, color);

                if (placed.Accidental is { } accidental)
                {
                    var x = chordX - 0.25f * s - (placed.AccidentalColumn + 1) * AccidentalColumnWidth * s;
                    _glyphs.Draw(ds, Smufl.Accidental(accidental), x, y, fontSize, color);
                }
            }
        }
    }

    public void Dispose() => _glyphs.Dispose();
}
