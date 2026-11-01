using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace PianoPractice.App.Rendering;

/// <summary>Draws Bravura glyphs positioned by their SMuFL origin (baseline) instead of the text box top.</summary>
public sealed class SmuflText : IDisposable
{
    private readonly Dictionary<float, (CanvasTextFormat Format, float Baseline)> _bySize = new();

    /// <summary>Draws <paramref name="glyph"/> with its origin at (x, y). Font size is in DIPs (4 × staff space for normal size).</summary>
    public void Draw(CanvasDrawingSession ds, string glyph, float x, float y, float fontSize, Color color)
    {
        var (format, baseline) = Get(ds, fontSize);
        ds.DrawText(glyph, x, y - baseline, color, format);
    }

    private (CanvasTextFormat, float) Get(ICanvasResourceCreator device, float fontSize)
    {
        if (!_bySize.TryGetValue(fontSize, out var entry))
        {
            var format = new CanvasTextFormat
            {
                FontFamily = Smufl.FontUri,
                FontSize = fontSize,
                WordWrapping = CanvasWordWrapping.NoWrap,
            };
            // The baseline is a font-wide metric, so any glyph will do.
            using var layout = new CanvasTextLayout(device, Smufl.NoteheadBlack, format, 0, 0);
            entry = (format, layout.LineMetrics[0].Baseline);

            if (_bySize.Count > 16) Clear(); // sizes change on resize; don't grow forever
            _bySize[fontSize] = entry;
        }
        return entry;
    }

    private void Clear()
    {
        foreach (var (format, _) in _bySize.Values) format.Dispose();
        _bySize.Clear();
    }

    public void Dispose() => Clear();
}
