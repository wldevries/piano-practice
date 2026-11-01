using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PianoPractice.App.Rendering;
using PianoPractice.Core.PlayAlong;
using PianoPractice.Core.Theory;
using Windows.Foundation;
using Windows.UI;

namespace PianoPractice.App.Controls;

/// <summary>
/// Notes falling towards the keyboard: each note's column matches its key on a <see cref="PianoKeyboard"/>
/// with the same <see cref="LowestKey"/>/<see cref="HighestKey"/> and width, and it reaches the bottom edge
/// when it should be played. Call <see cref="Refresh"/> every frame while playing.
/// </summary>
public sealed partial class FallingNotes : UserControl
{
    private static readonly Color BackgroundColor = Color.FromArgb(255, 26, 27, 33);
    private static readonly Color Lane = Color.FromArgb(255, 34, 36, 44);
    private static readonly Color OctaveLine = Color.FromArgb(255, 52, 55, 66);
    private static readonly Color BarLine = Color.FromArgb(255, 82, 86, 100);
    private static readonly Color BarLabel = Color.FromArgb(255, 130, 134, 150);
    private static readonly Color MissedColor = Color.FromArgb(255, 239, 68, 68);
    private static readonly Color OutOfRangeColor = Color.FromArgb(255, 90, 92, 100);
    private static readonly Color Shade = Color.FromArgb(150, 10, 10, 14);

    private KeyboardLayout? _layout;
    private Dictionary<int, Rect> _keyRects = [];
    private CanvasTextFormat? _noteFormat;
    private CanvasTextFormat? _barFormat;
    private CanvasTextFormat? _messageFormat;

    public FallingNotes()
    {
        InitializeComponent();
        SizeChanged += (_, _) => { _layout = null; Canvas.Invalidate(); };
    }

    /// <summary>What to draw; null shows <see cref="EmptyText"/>.</summary>
    public PlayAlongEngine? Engine { get; set; }

    public string EmptyText { get; set; } = "Choose a song";

    /// <summary>How many real seconds of music are visible above the keyboard.</summary>
    public double LookAheadSeconds { get; set; } = 3;

    public bool ShowNoteNames { get; set; } = true;

    public int LowestKey
    {
        get => (int)GetValue(LowestKeyProperty);
        set => SetValue(LowestKeyProperty, value);
    }

    public static readonly DependencyProperty LowestKeyProperty = DependencyProperty.Register(
        nameof(LowestKey), typeof(int), typeof(FallingNotes), new PropertyMetadata(Pitch.PianoLowest, OnLayoutChanged));

    public int HighestKey
    {
        get => (int)GetValue(HighestKeyProperty);
        set => SetValue(HighestKeyProperty, value);
    }

    public static readonly DependencyProperty HighestKeyProperty = DependencyProperty.Register(
        nameof(HighestKey), typeof(int), typeof(FallingNotes), new PropertyMetadata(Pitch.PianoHighest, OnLayoutChanged));

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (FallingNotes)d;
        control._layout = null;
        control.Canvas.Invalidate();
    }

    public void Refresh() => Canvas.Invalidate();

    private KeyboardLayout EnsureLayout(double width, double height)
    {
        if (_layout is null || Math.Abs(_layout.WhiteKeyWidth * _layout.WhiteKeys.Count - width) > 0.5)
        {
            _layout = new KeyboardLayout(LowestKey, HighestKey, width, height);
            _keyRects = _layout.WhiteKeys.Concat(_layout.BlackKeys).ToDictionary(k => k.Midi, k => k.Rect);
        }
        return _layout;
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var width = sender.ActualWidth;
        var height = sender.ActualHeight;
        if (width < 1 || height < 1) return;
        var layout = EnsureLayout(width, height);

        _noteFormat ??= new CanvasTextFormat
        {
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Bottom,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        _barFormat ??= new CanvasTextFormat { FontSize = 11, VerticalAlignment = CanvasVerticalAlignment.Bottom };
        _messageFormat ??= new CanvasTextFormat
        {
            FontSize = 16,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
        };

        ds.FillRectangle(0, 0, (float)width, (float)height, BackgroundColor);

        // Lanes above the black keys and a line at every C and F help the eye find the key.
        foreach (var (_, rect) in layout.BlackKeys) ds.FillRectangle(new Rect(rect.X, 0, rect.Width, height), Lane);
        foreach (var (midi, rect) in layout.WhiteKeys)
        {
            if (midi % 12 is 0 or 5) ds.DrawLine((float)rect.X, 0, (float)rect.X, (float)height, OctaveLine, midi % 12 == 0 ? 1.5f : 0.75f);
        }

        var engine = Engine;
        if (engine is null)
        {
            ds.DrawText(EmptyText, new Rect(0, 0, width, height), BarLabel, _messageFormat);
            return;
        }

        var song = engine.Song;
        var position = engine.Position;
        var pixelsPerSecond = height / (LookAheadSeconds * engine.Tempo);
        double Y(double time) => height - (time - position) * pixelsPerSecond;

        // Bar lines with numbers.
        for (var bar = 1; bar <= song.BarCount; bar++)
        {
            var y = Y(song.BarStarts[bar - 1]);
            if (y < -20 || y > height) continue;
            var inRange = bar >= engine.FromBar && bar <= engine.ToBar;
            ds.DrawLine(0, (float)y, (float)width, (float)y, inRange ? BarLine : OctaveLine, 1);
            ds.DrawText(bar.ToString(), new Rect(4, y - 16, 40, 15), BarLabel, _barFormat);
        }

        var waitingAt = engine.IsWaiting ? engine.NextChordStart : null;
        var notes = song.Notes;
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var bottom = Y(note.Start);
            if (bottom < 0) break; // sorted by start: the rest is further up
            var top = Y(note.End);
            if (top > height) continue;
            if (!_keyRects.TryGetValue(note.Pitch.MidiNumber, out var key)) continue;

            var status = engine.StatusOf(i);
            var black = note.Pitch.IsBlackKey;
            var hand = note.Hand == Hand.Right ? PianoKeyboard.RightHandColor : PianoKeyboard.LeftHandColor;
            if (black) hand = Darken(hand, 0.75f);

            var inset = black ? 0.5 : 2;
            var rect = new Rect(key.X + inset, top + 1, Math.Max(key.Width - 2 * inset, 2), Math.Max(bottom - top - 2, 3));

            switch (status)
            {
                case NoteStatus.Upcoming:
                    ds.FillRoundedRectangle(rect, 4, 4, hand);
                    break;
                case NoteStatus.Hit:
                    ds.FillRoundedRectangle(rect, 4, 4, WithAlpha(hand, 90));
                    break;
                case NoteStatus.Missed:
                    ds.FillRoundedRectangle(rect, 4, 4, WithAlpha(MissedColor, 110));
                    ds.DrawRoundedRectangle(rect, 4, 4, MissedColor, 1.5f);
                    break;
                case NoteStatus.Accompaniment:
                    ds.FillRoundedRectangle(rect, 4, 4, WithAlpha(hand, 70));
                    ds.DrawRoundedRectangle(rect, 4, 4, WithAlpha(hand, 170), 1);
                    break;
                case NoteStatus.Silent:
                    ds.DrawRoundedRectangle(rect, 4, 4, WithAlpha(hand, 110), 1);
                    break;
                default:
                    ds.FillRoundedRectangle(rect, 4, 4, WithAlpha(OutOfRangeColor, 50));
                    break;
            }

            if (status == NoteStatus.Upcoming && waitingAt is { } w && Math.Abs(note.Start - w) < 0.05)
            {
                ds.DrawRoundedRectangle(rect, 4, 4, Colors.White, 2.5f);
            }

            if (ShowNoteNames && status is NoteStatus.Upcoming or NoteStatus.Accompaniment && rect.Height >= 16 && rect.Width >= 9)
            {
                var label = NoteNames.PitchClass(note.Pitch.PitchClass);
                var textColor = status == NoteStatus.Upcoming ? Colors.White : WithAlpha(Colors.White, 150);
                ds.DrawText(label, new Rect(rect.X - 6, rect.Y, rect.Width + 12, rect.Height - 2), textColor, _noteFormat);
            }
        }

        // Darken what lies outside the bar range being practised.
        var rangeTop = Y(engine.RangeEnd);
        if (rangeTop > 0) ds.FillRectangle(0, 0, (float)width, (float)Math.Min(rangeTop, height), Shade);
        var rangeBottom = Math.Max(Y(engine.RangeStart), 0);
        if (rangeBottom < height) ds.FillRectangle(0, (float)rangeBottom, (float)width, (float)(height - rangeBottom), Shade);

        // The "now" line where notes meet the keyboard; white while the song waits for you.
        ds.FillRectangle(0, (float)height - 3, (float)width, 3, engine.IsWaiting ? Colors.White : Color.FromArgb(255, 120, 124, 140));
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Darken(Color c, float factor) =>
        Color.FromArgb(c.A, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _noteFormat?.Dispose();
        _barFormat?.Dispose();
        _messageFormat?.Dispose();
        _noteFormat = _barFormat = _messageFormat = null;
        Canvas.RemoveFromVisualTree();
    }
}
