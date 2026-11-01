using Windows.Foundation;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.Rendering;

/// <summary>Key rectangles for a piano keyboard laid out in a given size.</summary>
public sealed class KeyboardLayout
{
    // Black keys aren't centred on the gap between white keys on a real piano;
    // these nudges (in black-key widths) mimic the 2+3 grouping.
    private static readonly Dictionary<int, double> BlackKeyOffset = new()
    {
        [1] = -0.15, [3] = 0.15,               // C#, D#
        [6] = -0.2, [8] = 0, [10] = 0.2,        // F#, G#, A#
    };

    public const double BlackKeyWidthRatio = 0.58;
    public const double BlackKeyHeightRatio = 0.63;

    private readonly List<(int Midi, Rect Rect)> _white = [];
    private readonly List<(int Midi, Rect Rect)> _black = [];

    public KeyboardLayout(int lowest, int highest, double width, double height)
    {
        Lowest = lowest;
        Highest = highest;

        var whiteCount = Enumerable.Range(lowest, highest - lowest + 1).Count(m => !new Pitch(m).IsBlackKey);
        WhiteKeyWidth = whiteCount == 0 ? 0 : width / whiteCount;
        var blackWidth = WhiteKeyWidth * BlackKeyWidthRatio;
        var blackHeight = height * BlackKeyHeightRatio;

        var whiteIndex = 0;
        for (var midi = lowest; midi <= highest; midi++)
        {
            var pitch = new Pitch(midi);
            if (pitch.IsBlackKey)
            {
                // Centred on the boundary after the previous white key, then nudged.
                var centre = whiteIndex * WhiteKeyWidth + BlackKeyOffset[pitch.PitchClass] * blackWidth;
                _black.Add((midi, new Rect(centre - blackWidth / 2, 0, blackWidth, blackHeight)));
            }
            else
            {
                _white.Add((midi, new Rect(whiteIndex * WhiteKeyWidth, 0, WhiteKeyWidth, height)));
                whiteIndex++;
            }
        }
    }

    public int Lowest { get; }
    public int Highest { get; }
    public double WhiteKeyWidth { get; }
    public IReadOnlyList<(int Midi, Rect Rect)> WhiteKeys => _white;
    public IReadOnlyList<(int Midi, Rect Rect)> BlackKeys => _black;

    /// <summary>Key under a point; black keys sit on top so they're tested first.</summary>
    public int? HitTest(Point p)
    {
        foreach (var (midi, rect) in _black)
        {
            if (rect.Contains(p)) return midi;
        }
        foreach (var (midi, rect) in _white)
        {
            if (rect.Contains(p)) return midi;
        }
        return null;
    }
}
