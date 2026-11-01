using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PianoPractice.App.Rendering;
using PianoPractice.Core.Notation;
using PianoPractice.Core.PlayAlong;
using Windows.Foundation;
using Windows.UI;

namespace PianoPractice.App.Controls;

/// <summary>
/// The song as a scrolling grand staff: right hand on the treble staff, left hand on the bass staff.
/// Notes move right to left and are to be played when they reach the playhead. Spacing is
/// proportional to time, so it scrolls at a steady speed. Call <see cref="Refresh"/> every frame while playing.
/// </summary>
public sealed partial class SheetMusic : UserControl
{
    private static readonly Color Paper = Color.FromArgb(255, 250, 248, 242);
    private static readonly Color Ink = Color.FromArgb(255, 28, 28, 30);
    private static readonly Color HitColor = Color.FromArgb(255, 22, 150, 70);
    private static readonly Color MissedColor = Color.FromArgb(255, 220, 50, 50);
    private static readonly Color OtherHandColor = Color.FromArgb(255, 150, 150, 155);
    private static readonly Color MutedColor = Color.FromArgb(255, 190, 190, 195);
    private static readonly Color OutOfRangeColor = Color.FromArgb(255, 205, 205, 208);
    private static readonly Color PlayheadColor = Color.FromArgb(40, 56, 152, 236);
    private static readonly Color WaitingPlayheadColor = Color.FromArgb(70, 56, 152, 236);
    private static readonly Color Shade = Color.FromArgb(110, 225, 222, 215);
    private static readonly Color BarNumberColor = Color.FromArgb(255, 130, 130, 135);

    /// <summary>Staff spaces between the closest chords: keeps sixteenths readable.</summary>
    private const float MinChordSpacing = 3.2f;
    /// <summary>Upper limit for the staff space, in DIPs.</summary>
    private const float MaxStaffSpace = 15f;

    private readonly SmuflText _glyphs = new();
    private CanvasTextFormat? _textFormat;

    private Song? _song;
    private Score? _score;
    private double _spacesPerSecond;
    private int _trebleHigh, _trebleLow, _bassHigh, _bassLow;
    private List<(int First, int Last)> _ottavaRuns = [];
    private CanvasStrokeStyle? _dashed;

    public SheetMusic()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Canvas.Invalidate();
    }

    /// <summary>What to draw; null shows <see cref="EmptyText"/>.</summary>
    public PlayAlongEngine? Engine { get; set; }

    public string EmptyText { get; set; } = "Choose a song";

    public void Refresh() => Canvas.Invalidate();

    private void EnsureScore(Song song)
    {
        if (ReferenceEquals(song, _song)) return;
        _song = song;
        _score = ScoreBuilder.Build(song);

        // Spacing from the shortest common gap between chords (10th percentile, so a few grace-like notes don't squash everything).
        var onsets = _score.Chords.Select(c => c.Start).Distinct().Order().ToList();
        var gaps = onsets.Zip(onsets.Skip(1), (a, b) => b - a).Where(g => g > 0.02).Order().ToList();
        var shortest = gaps.Count == 0 ? 0.5 : gaps[gaps.Count / 10];
        _spacesPerSecond = MinChordSpacing / Math.Max(shortest, 0.06);

        int Extreme(Clef clef, Func<ScoreChord, int> pick, Func<IEnumerable<int>, int> agg, int fallback)
        {
            var steps = _score.Chords.Where(c => c.Clef == clef).Select(pick).ToList();
            return steps.Count == 0 ? fallback : agg(steps);
        }
        _trebleHigh = Extreme(Clef.Treble, c => c.HighestStep, s => s.Max(), 8);
        _trebleLow = Extreme(Clef.Treble, c => c.LowestStep, s => s.Min(), 0);
        _bassHigh = Extreme(Clef.Bass, c => c.HighestStep, s => s.Max(), 8);
        _bassLow = Extreme(Clef.Bass, c => c.LowestStep, s => s.Min(), 0);

        // Runs of consecutive right-hand chords under one 8va line.
        _ottavaRuns = [];
        var treble = Enumerable.Range(0, _score.Chords.Count).Where(i => _score.Chords[i].Hand == Hand.Right).ToList();
        for (var k = 0; k < treble.Count; k++)
        {
            if (!_score.Chords[treble[k]].Ottava) continue;
            if (k > 0 && _score.Chords[treble[k - 1]].Ottava) _ottavaRuns[^1] = (_ottavaRuns[^1].First, treble[k]);
            else _ottavaRuns.Add((treble[k], treble[k]));
        }
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        if (width < 1 || height < 1) return;

        ds.FillRectangle(0, 0, width, height, Paper);
        _textFormat ??= new CanvasTextFormat { FontSize = 12, WordWrapping = CanvasWordWrapping.NoWrap };

        var engine = Engine;
        if (engine is null)
        {
            using var format = new CanvasTextFormat
            {
                FontSize = 16,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center,
            };
            ds.DrawText(EmptyText, new Rect(0, 0, width, height), BarNumberColor, format);
            return;
        }

        EnsureScore(engine.Song);
        var score = _score!;
        var song = engine.Song;

        // Vertical layout: room for the highest and lowest notes (with stems), clamped so outliers don't shrink the staff to nothing.
        var above = Math.Clamp((_trebleHigh - 8) / 2f, 0, 6) + 4f;
        var gap = Math.Clamp(-_trebleLow / 2f + (_bassHigh - 8) / 2f + 3f, 6f, 12f);
        var below = Math.Clamp(-_bassLow / 2f, 0, 6) + 4f;
        var total = above + 4 + gap + 4 + below;
        var s = Math.Clamp(height / total, 5f, MaxStaffSpace);
        var fontSize = 4 * s;

        var top = (height - total * s) / 2;
        var trebleTop = top + above * s;
        var trebleBottom = trebleTop + 4 * s;
        var bassTop = trebleBottom + gap * s;
        var bassBottom = bassTop + 4 * s;
        float Y(Clef clef, int step) => (clef == Clef.Treble ? trebleBottom : bassBottom) - step * s / 2f;

        // Horizontal layout: header (brace, clefs, key), then the playhead; time runs to the right.
        var braceSize = bassBottom - trebleTop;
        var left = Smufl.BraceWidth * braceSize / 4 + 0.8f * s;
        var keyX = left + 4.2f * s;
        var headerEnd = keyX + Math.Abs(score.Key.Fifths) * s + 1.2f * s;
        var playX = headerEnd + 4f * s;
        var pxPerSecond = _spacesPerSecond * s;
        var position = engine.Position;
        float X(double time) => (float)(playX + (time - position) * pxPerSecond);

        // Staves.
        foreach (var staffTop in new[] { trebleTop, bassTop })
        {
            for (var i = 0; i < 5; i++) ds.DrawLine(left, staffTop + i * s, width, staffTop + i * s, Ink, Smufl.StaffLineThickness * s);
        }

        // Playhead: where notes should be played. Stronger while the song waits.
        ds.FillRectangle(playX - 1.2f * s, trebleTop - 2 * s, 2.4f * s, bassBottom - trebleTop + 4 * s, engine.IsWaiting ? WaitingPlayheadColor : PlayheadColor);

        // Bar lines and numbers.
        for (var bar = 1; bar <= song.BarCount; bar++)
        {
            var x = X(song.BarStarts[bar - 1]) - 1.6f * s;
            if (x < headerEnd || x > width) continue;
            if (bar > 1) ds.DrawLine(x, trebleTop, x, bassBottom, Ink, Smufl.ThinBarlineThickness * s);
            ds.DrawText(bar.ToString(), x + 2, trebleTop - 1.4f * s - 14, BarNumberColor, _textFormat);
        }

        // Shade bars outside the practised range.
        var rangeStartX = X(engine.RangeStart) - 1.6f * s;
        var rangeEndX = X(engine.RangeEnd) - 1.6f * s;
        if (rangeStartX > headerEnd) ds.FillRectangle(headerEnd, 0, rangeStartX - headerEnd, height, Shade);
        if (rangeEndX < width) ds.FillRectangle(Math.Max(rangeEndX, headerEnd), 0, width - Math.Max(rangeEndX, headerEnd), height, Shade);

        var visibleFrom = position - (playX - headerEnd + 4 * s) / pxPerSecond;
        var visibleTo = position + (width - playX + 4 * s) / pxPerSecond;

        foreach (var rest in score.Rests)
        {
            if (rest.BarEnd < visibleFrom || rest.Start > visibleTo) continue;
            DrawRest(ds, rest, X, Y, s, fontSize, engine);
        }

        var waitingAt = engine.IsWaiting ? engine.NextChordStart : null;
        var drawnBeams = new HashSet<int>();
        for (var i = 0; i < score.Chords.Count; i++)
        {
            var chord = score.Chords[i];
            if (chord.Start < visibleFrom - 2 || chord.Start > visibleTo) continue;
            if (chord.Beam >= 0)
            {
                if (drawnBeams.Add(chord.Beam)) DrawBeamGroup(ds, score, score.Beams[chord.Beam], X, Y, s, fontSize, engine, waitingAt);
            }
            else
            {
                var color = ChordColor(chord, engine, waitingAt);
                var stemEnd = StemEnd(chord, Y, s, extraFlags: chord.Value.Flags);
                DrawChord(ds, chord, X(chord.Start), Y, s, fontSize, engine, waitingAt, stemEnd, color);
                if (chord.Value.Flags > 0) DrawFlag(ds, chord, StemX(chord, X(chord.Start), s), stemEnd, fontSize, color);
            }
        }

        // 8va lines: "8va" and a dashed line over the passage, with a hook at the end.
        _dashed ??= new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        foreach (var (firstIndex, lastIndex) in _ottavaRuns)
        {
            var first = score.Chords[firstIndex];
            var last = score.Chords[lastIndex];
            if (last.Start < visibleFrom - 2 || first.Start > visibleTo) continue;
            var run = score.Chords.Skip(firstIndex).Take(lastIndex - firstIndex + 1).Where(c => c.Hand == Hand.Right).ToList();
            var lineY = Math.Min(run.Min(c => Y(Clef.Treble, c.HighestStep) - (c.StemUp ? 4.5f : 1.5f) * s), trebleTop - 2f * s);
            var x1 = X(first.Start) - s;
            var x2 = X(last.Start) + s;
            ds.DrawText("8va", x1, lineY - 16, Ink, _textFormat);
            ds.DrawLine(x1 + 26, lineY - 6, x2, lineY - 6, Ink, 1, _dashed);
            ds.DrawLine(x2, lineY - 6, x2, lineY + 2, Ink, 1);
        }

        // Header on top: notes scroll away underneath it.
        ds.FillRectangle(0, 0, headerEnd, height, Paper);
        foreach (var staffTop in new[] { trebleTop, bassTop })
        {
            for (var i = 0; i < 5; i++) ds.DrawLine(left, staffTop + i * s, headerEnd, staffTop + i * s, Ink, Smufl.StaffLineThickness * s);
        }
        ds.DrawLine(left, trebleTop, left, bassBottom, Ink, Smufl.ThinBarlineThickness * s);
        _glyphs.Draw(ds, Smufl.Brace, left - 0.4f * s - Smufl.BraceWidth * braceSize / 4, bassBottom, braceSize, Ink);
        _glyphs.Draw(ds, Smufl.GClef, left + 0.8f * s, Y(Clef.Treble, StaffPosition.ClefLine(Clef.Treble)), fontSize, Ink);
        _glyphs.Draw(ds, Smufl.FClef, left + 0.8f * s, Y(Clef.Bass, StaffPosition.ClefLine(Clef.Bass)), fontSize, Ink);
        var keyGlyph = score.Key.Fifths >= 0 ? Smufl.AccidentalSharp : Smufl.AccidentalFlat;
        foreach (var clef in new[] { Clef.Treble, Clef.Bass })
        {
            var steps = StaffPosition.KeySignatureSteps(score.Key, clef);
            for (var k = 0; k < steps.Count; k++) _glyphs.Draw(ds, keyGlyph, keyX + k * s, Y(clef, steps[k]), fontSize, Ink);
        }
    }

    private static Color NoteColor(NoteStatus status, Hand hand, bool waiting) => status switch
    {
        NoteStatus.Upcoming => waiting ? (hand == Hand.Right ? PianoKeyboard.RightHandColor : PianoKeyboard.LeftHandColor) : Ink,
        NoteStatus.Hit => HitColor,
        NoteStatus.Missed => MissedColor,
        NoteStatus.Accompaniment => OtherHandColor,
        NoteStatus.Silent => MutedColor,
        _ => OutOfRangeColor,
    };

    private static bool IsWaitingChord(ScoreChord chord, double? waitingAt) => waitingAt is { } w && Math.Abs(chord.Start - w) < 0.05;

    /// <summary>Stem and beam colour: ink while anything is left to play, else the colour of the notes.</summary>
    private static Color ChordColor(ScoreChord chord, PlayAlongEngine engine, double? waitingAt)
    {
        var statuses = chord.NoteIndices.Select(engine.StatusOf).ToList();
        var status = statuses.Contains(NoteStatus.Upcoming) ? NoteStatus.Upcoming : statuses[0];
        return NoteColor(status, chord.Hand, IsWaitingChord(chord, waitingAt));
    }

    private static float HeadWidth(ScoreChord chord, float s) =>
        (chord.Value.Kind == NoteValueKind.Whole ? Smufl.NoteheadWholeWidth : Smufl.NoteheadBlackWidth) * s;

    private static float StemX(ScoreChord chord, float x, float s)
    {
        var headLeft = x - HeadWidth(chord, s) / 2;
        return chord.StemUp ? headLeft + HeadWidth(chord, s) - Smufl.StemThickness * s / 2 : headLeft + Smufl.StemThickness * s / 2;
    }

    /// <summary>Where an unbeamed stem ends: 3.5 spaces from the note, and at least to the middle line.</summary>
    private static float StemEnd(ScoreChord chord, Func<Clef, int, float> y, float s, int extraFlags)
    {
        var length = (3.5f + Math.Max(extraFlags - 1, 0) * 0.5f) * s;
        var middle = y(chord.Clef, StaffPosition.MiddleLine);
        return chord.StemUp
            ? Math.Min(y(chord.Clef, chord.HighestStep) - length, middle)
            : Math.Max(y(chord.Clef, chord.LowestStep) + length, middle);
    }

    private void DrawChord(CanvasDrawingSession ds, ScoreChord chord, float x, Func<Clef, int, float> y, float s, float fontSize,
        PlayAlongEngine engine, double? waitingAt, float stemEnd, Color stemColor)
    {
        var clef = chord.Clef;
        var headWidth = HeadWidth(chord, s);
        var headLeft = x - headWidth / 2;
        var waiting = IsWaitingChord(chord, waitingAt);
        var layout = chord.Layout;

        var head = chord.Value.Kind switch
        {
            NoteValueKind.Whole => Smufl.NoteheadWhole,
            NoteValueKind.Half => Smufl.NoteheadHalf,
            _ => Smufl.NoteheadBlack,
        };

        var ledgerRight = headLeft + headWidth * (layout.HasDisplacedNotes ? 2 : 1) + Smufl.LedgerLineExtension * s;
        foreach (var step in layout.LedgerLines)
        {
            ds.DrawLine(headLeft - Smufl.LedgerLineExtension * s, y(clef, step), ledgerRight, y(clef, step), stemColor, Smufl.LedgerLineThickness * s);
        }

        foreach (var placed in layout.Notes)
        {
            var color = NoteColor(engine.StatusOf(chord.NoteIndices[placed.InputIndex]), chord.Hand, waiting);
            var noteY = y(clef, placed.Step);
            var noteX = headLeft + (placed.Displaced ? headWidth : 0);
            _glyphs.Draw(ds, head, noteX, noteY, fontSize, color);

            if (placed.Accidental is { } accidental)
            {
                var ax = headLeft - 0.25f * s - (placed.AccidentalColumn + 1) * 1.15f * s;
                _glyphs.Draw(ds, Smufl.Accidental(accidental), ax, noteY, fontSize, color);
            }
            if (chord.Value.Dots > 0)
            {
                var dotStep = placed.Step % 2 == 0 ? placed.Step + 1 : placed.Step; // dots sit in a space
                var dx = headLeft + headWidth * (layout.HasDisplacedNotes ? 2 : 1) + 0.35f * s;
                _glyphs.Draw(ds, Smufl.AugmentationDot, dx, y(clef, dotStep), fontSize, color);
            }
        }

        if (chord.Value.HasStem)
        {
            var stemX = StemX(chord, x, s);
            var from = chord.StemUp ? y(clef, chord.LowestStep) - 0.17f * s : y(clef, chord.HighestStep) + 0.17f * s;
            ds.DrawLine(stemX, from, stemX, stemEnd, stemColor, Smufl.StemThickness * s);
        }
    }

    private void DrawFlag(CanvasDrawingSession ds, ScoreChord chord, float stemX, float stemEnd, float fontSize, Color color)
    {
        var glyph = (chord.Value.Flags, chord.StemUp) switch
        {
            (1, true) => Smufl.Flag8thUp,
            (1, false) => Smufl.Flag8thDown,
            (2, true) => Smufl.Flag16thUp,
            (2, false) => Smufl.Flag16thDown,
            (_, true) => Smufl.Flag32ndUp,
            _ => Smufl.Flag32ndDown,
        };
        _glyphs.Draw(ds, glyph, stemX - Smufl.StemThickness * fontSize / 8, stemEnd, fontSize, color);
    }

    private void DrawBeamGroup(CanvasDrawingSession ds, Score score, ScoreBeam beam, Func<double, float> x, Func<Clef, int, float> y,
        float s, float fontSize, PlayAlongEngine engine, double? waitingAt)
    {
        var chords = beam.Chords.Select(i => score.Chords[i]).ToList();
        var maxFlags = chords.Max(c => c.Value.Flags);
        var clef = chords[0].Clef;

        // Flat beam, clear of every note in the group.
        var extra = (3.25f + Math.Max(maxFlags - 1, 0) * 0.75f) * s;
        var middle = y(clef, StaffPosition.MiddleLine);
        var beamY = beam.StemUp
            ? Math.Min(chords.Min(c => y(clef, c.HighestStep)) - extra, middle)
            : Math.Max(chords.Max(c => y(clef, c.LowestStep)) + extra, middle);

        var stems = chords.Select(c => StemX(c, x(c.Start), s)).ToList();
        var colors = chords.Select(c => ChordColor(c, engine, waitingAt)).ToList();
        for (var i = 0; i < chords.Count; i++)
        {
            DrawChord(ds, chords[i], x(chords[i].Start), y, s, fontSize, engine, waitingAt, beamY, colors[i]);
        }

        var thickness = Smufl.BeamThickness * s;
        var step = (Smufl.BeamThickness + Smufl.BeamSpacing) * s * (beam.StemUp ? 1 : -1);
        float Level(int level) => beam.StemUp ? beamY + level * step : beamY + level * step - thickness;
        var beamColor = colors.Contains(Ink) ? Ink : colors[0];

        var half = Smufl.StemThickness * s / 2;
        ds.FillRectangle(stems[0] - half, Level(0), stems[^1] - stems[0] + 2 * half, thickness, beamColor);

        // Secondary beams between neighbours that both have them; a stub for a lone sixteenth.
        for (var level = 1; level < maxFlags; level++)
        {
            for (var i = 0; i < chords.Count; i++)
            {
                if (chords[i].Value.Flags <= level) continue;
                var nextHas = i + 1 < chords.Count && chords[i + 1].Value.Flags > level;
                var previousHas = i > 0 && chords[i - 1].Value.Flags > level;
                if (nextHas)
                {
                    ds.FillRectangle(stems[i] - half, Level(level), stems[i + 1] - stems[i] + 2 * half, thickness, beamColor);
                }
                else if (!previousHas)
                {
                    var stub = 1.1f * s;
                    var fromX = i > 0 ? stems[i] - stub : stems[i];
                    ds.FillRectangle(fromX - half, Level(level), stub + 2 * half, thickness, beamColor);
                }
            }
        }
    }

    private void DrawRest(CanvasDrawingSession ds, ScoreRest rest, Func<double, float> x, Func<Clef, int, float> y, float s, float fontSize, PlayAlongEngine engine)
    {
        var clef = rest.Hand == Hand.Right ? Clef.Treble : Clef.Bass;
        var color = engine.Setup.Practises(rest.Hand) ? Ink : OtherHandColor;
        if (rest.WholeBar)
        {
            var centre = (x(rest.Start) + x(rest.BarEnd)) / 2 - 1.6f * s;
            _glyphs.Draw(ds, Smufl.RestWhole, centre - 0.6f * s, y(clef, 6), fontSize, color);
            return;
        }

        var glyph = rest.Value.Kind switch
        {
            NoteValueKind.Whole => Smufl.RestWhole,
            NoteValueKind.Half => Smufl.RestHalf,
            NoteValueKind.Quarter => Smufl.RestQuarter,
            NoteValueKind.Eighth => Smufl.Rest8th,
            NoteValueKind.Sixteenth => Smufl.Rest16th,
            _ => Smufl.Rest32nd,
        };
        var step = rest.Value.Kind == NoteValueKind.Whole ? 6 : StaffPosition.MiddleLine;
        _glyphs.Draw(ds, glyph, x(rest.Start) - 0.6f * s, y(clef, step), fontSize, color);
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _textFormat?.Dispose();
        _dashed?.Dispose();
        _dashed = null;
        _textFormat = null;
        _glyphs.Dispose();
        Canvas.RemoveFromVisualTree();
    }
}
