using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using PianoPractice.Core.Theory;
using Windows.UI;

namespace PianoPractice.App.Rendering;

/// <summary>
/// Minimal single-staff renderer (treble clef). M0 proof that Bravura renders
/// through Win2D at the right positions; M2 grows this into a full grand staff.
/// </summary>
public sealed class StaffRenderer
{
    // E4 sits on the bottom line of the treble staff.
    private static readonly int TrebleBottomLineIndex = new SpelledPitch(Letter.E, 0, 4).DiatonicIndex;

    private readonly Dictionary<(string Glyph, float Size), float> _baselineCache = new();

    public float StaffSpace { get; set; } = 16f;
    public Color Ink { get; set; } = Color.FromArgb(255, 0, 0, 0);

    /// <summary>Draws a five-line treble staff with its top-left at <paramref name="origin"/>.</summary>
    public void DrawTrebleStaff(CanvasDrawingSession ds, Vector2 origin, float width, IReadOnlyList<SpelledPitch> notes)
    {
        var s = StaffSpace;
        var bottomLineY = origin.Y + 4 * s;

        for (var i = 0; i < 5; i++)
        {
            var y = origin.Y + i * s;
            ds.DrawLine(origin.X, y, origin.X + width, y, Ink, Smufl.StaffLineThickness * s);
        }

        // Treble clef origin sits on the G4 line (second line from the bottom).
        DrawGlyph(ds, Smufl.GClef, origin.X + 0.5f * s, bottomLineY - s);

        var x = origin.X + 5f * s;
        foreach (var note in notes)
        {
            DrawQuarterNote(ds, note, x, bottomLineY);
            x += 3.5f * s;
        }
    }

    private void DrawQuarterNote(CanvasDrawingSession ds, SpelledPitch note, float x, float bottomLineY)
    {
        var s = StaffSpace;
        var step = note.DiatonicIndex - TrebleBottomLineIndex; // 0 = bottom line, 8 = top line
        var y = bottomLineY - step * s / 2f;

        DrawLedgerLines(ds, step, x, bottomLineY);

        if (note.Accidental != 0)
        {
            DrawGlyph(ds, AccidentalGlyph(note.Accidental), x - 1.4f * s, y);
        }

        DrawGlyph(ds, Smufl.NoteheadBlack, x, y);

        // Stem up for notes below the middle line, down otherwise.
        var stemWidth = Smufl.StemThickness * s;
        if (step < 4)
        {
            var stemX = x + Smufl.NoteheadBlackWidth * s - stemWidth / 2;
            ds.DrawLine(stemX, y - 0.15f * s, stemX, y - Smufl.StemLength * s, Ink, stemWidth);
        }
        else
        {
            var stemX = x + stemWidth / 2;
            ds.DrawLine(stemX, y + 0.15f * s, stemX, y + Smufl.StemLength * s, Ink, stemWidth);
        }
    }

    private void DrawLedgerLines(CanvasDrawingSession ds, int step, float x, float bottomLineY)
    {
        var s = StaffSpace;
        var left = x - Smufl.LedgerLineExtension * s;
        var right = x + (Smufl.NoteheadBlackWidth + Smufl.LedgerLineExtension) * s;
        var thickness = Smufl.LedgerLineThickness * s;

        for (var ledger = -2; ledger >= step; ledger -= 2)
        {
            var y = bottomLineY - ledger * s / 2f;
            ds.DrawLine(left, y, right, y, Ink, thickness);
        }
        for (var ledger = 10; ledger <= step; ledger += 2)
        {
            var y = bottomLineY - ledger * s / 2f;
            ds.DrawLine(left, y, right, y, Ink, thickness);
        }
    }

    private static string AccidentalGlyph(int accidental) => accidental switch
    {
        -2 => Smufl.AccidentalDoubleFlat,
        -1 => Smufl.AccidentalFlat,
        1 => Smufl.AccidentalSharp,
        2 => Smufl.AccidentalDoubleSharp,
        _ => Smufl.AccidentalNatural,
    };

    /// <summary>Draws a SMuFL glyph with its baseline (the SMuFL origin) at <paramref name="y"/>.</summary>
    private void DrawGlyph(CanvasDrawingSession ds, string glyph, float x, float y)
    {
        var size = 4 * StaffSpace;
        using var format = CreateFormat(size);
        ds.DrawText(glyph, x, y - Baseline(ds, glyph, size, format), Ink, format);
    }

    private float Baseline(ICanvasResourceCreator device, string glyph, float size, CanvasTextFormat format)
    {
        if (!_baselineCache.TryGetValue((glyph, size), out var baseline))
        {
            using var layout = new CanvasTextLayout(device, glyph, format, 0, 0);
            baseline = layout.LineMetrics[0].Baseline;
            _baselineCache[(glyph, size)] = baseline;
        }
        return baseline;
    }

    private static CanvasTextFormat CreateFormat(float size) => new()
    {
        FontFamily = Smufl.FontUri,
        FontSize = size,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };
}
