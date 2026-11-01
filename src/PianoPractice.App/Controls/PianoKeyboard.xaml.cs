using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PianoPractice.App.Rendering;
using PianoPractice.Core.Theory;
using Windows.UI;

namespace PianoPractice.App.Controls;

/// <summary>
/// Win2D-drawn piano keyboard. Shows <see cref="Highlights"/> and raises
/// <see cref="KeyPressed"/>/<see cref="KeyReleased"/> for mouse/touch/pen input (with glissando).
/// Height follows the width so keys keep a piano-like aspect ratio.
/// </summary>
public sealed partial class PianoKeyboard : UserControl
{
    private const double WhiteKeyAspect = 5.5; // height / width of a white key

    private static readonly Color WhiteKey = Color.FromArgb(255, 250, 250, 250);
    private static readonly Color BlackKey = Color.FromArgb(255, 28, 28, 30);
    private static readonly Color KeyBorder = Color.FromArgb(255, 120, 120, 120);
    private static readonly Color LabelColor = Color.FromArgb(255, 130, 130, 130);

    private readonly Dictionary<uint, int> _pointerKeys = new();
    private KeyboardLayout? _layout;

    public PianoKeyboard()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            // Keep a natural key shape: derive height from width (clamped).
            var whiteKeys = Enumerable.Range(LowestKey, HighestKey - LowestKey + 1).Count(m => !new Pitch(m).IsBlackKey);
            var desired = Math.Clamp(e.NewSize.Width / Math.Max(whiteKeys, 1) * WhiteKeyAspect, 80, 260);
            if (Math.Abs(Height - desired) > 0.5 || double.IsNaN(Height)) Height = desired;
            _layout = null;
            Canvas.Invalidate();
        };
    }

    public event EventHandler<int>? KeyPressed;
    public event EventHandler<int>? KeyReleased;

    #region Dependency properties

    public int LowestKey
    {
        get => (int)GetValue(LowestKeyProperty);
        set => SetValue(LowestKeyProperty, value);
    }

    public static readonly DependencyProperty LowestKeyProperty = DependencyProperty.Register(
        nameof(LowestKey), typeof(int), typeof(PianoKeyboard), new PropertyMetadata(Pitch.PianoLowest, OnLayoutPropertyChanged));

    public int HighestKey
    {
        get => (int)GetValue(HighestKeyProperty);
        set => SetValue(HighestKeyProperty, value);
    }

    public static readonly DependencyProperty HighestKeyProperty = DependencyProperty.Register(
        nameof(HighestKey), typeof(int), typeof(PianoKeyboard), new PropertyMetadata(Pitch.PianoHighest, OnLayoutPropertyChanged));

    public IReadOnlyCollection<KeyHighlight>? Highlights
    {
        get => (IReadOnlyCollection<KeyHighlight>?)GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    public static readonly DependencyProperty HighlightsProperty = DependencyProperty.Register(
        nameof(Highlights), typeof(IReadOnlyCollection<KeyHighlight>), typeof(PianoKeyboard),
        new PropertyMetadata(null, (d, _) => ((PianoKeyboard)d).Canvas.Invalidate()));

    public bool ShowOctaveLabels
    {
        get => (bool)GetValue(ShowOctaveLabelsProperty);
        set => SetValue(ShowOctaveLabelsProperty, value);
    }

    public static readonly DependencyProperty ShowOctaveLabelsProperty = DependencyProperty.Register(
        nameof(ShowOctaveLabels), typeof(bool), typeof(PianoKeyboard),
        new PropertyMetadata(true, (d, _) => ((PianoKeyboard)d).Canvas.Invalidate()));

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var keyboard = (PianoKeyboard)d;
        keyboard._layout = null;
        keyboard.Canvas.Invalidate();
    }

    #endregion

    public static Color ColorFor(KeyHighlightKind kind) => kind switch
    {
        KeyHighlightKind.Played => Color.FromArgb(255, 59, 130, 246),   // blue
        KeyHighlightKind.Sounded => Color.FromArgb(255, 245, 158, 11),  // amber
        KeyHighlightKind.Correct => Color.FromArgb(255, 34, 197, 94),   // green
        KeyHighlightKind.Wrong => Color.FromArgb(255, 239, 68, 68),     // red
        _ => Color.FromArgb(255, 168, 85, 247),                          // purple hint
    };

    private KeyboardLayout Layout =>
        _layout ??= new KeyboardLayout(LowestKey, HighestKey, Canvas.ActualWidth, Canvas.ActualHeight);

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var layout = Layout;
        var highlights = new Dictionary<int, KeyHighlight>();
        foreach (var h in Highlights ?? []) highlights[h.MidiNumber] = h;

        using var labelFormat = new CanvasTextFormat
        {
            FontSize = (float)Math.Clamp(layout.WhiteKeyWidth * 0.45, 7, 13),
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Bottom,
        };

        foreach (var (midi, rect) in layout.WhiteKeys)
        {
            var fill = highlights.TryGetValue(midi, out var h) ? Blend(WhiteKey, ColorFor(h.Kind), h.Intensity) : WhiteKey;
            ds.FillRectangle(rect, fill);
            ds.DrawRectangle(rect, KeyBorder, 1);

            if (ShowOctaveLabels && midi % 12 == 0)
            {
                var labelRect = new Windows.Foundation.Rect(rect.X, rect.Y, rect.Width, rect.Height - 4);
                ds.DrawText(NoteNames.WithOctave(new Pitch(midi)), labelRect, LabelColor, labelFormat);
            }
        }

        foreach (var (midi, rect) in layout.BlackKeys)
        {
            var fill = highlights.TryGetValue(midi, out var h) ? Blend(BlackKey, ColorFor(h.Kind), h.Intensity) : BlackKey;
            ds.FillRoundedRectangle(rect, 2, 2, fill);
        }
    }

    /// <summary>Mixes the highlight into the key colour; low intensity still shows clearly.</summary>
    private static Color Blend(Color baseColor, Color highlight, float intensity)
    {
        var t = 0.55f + 0.45f * Math.Clamp(intensity, 0f, 1f);
        byte Mix(byte a, byte b) => (byte)(a + (b - a) * t);
        return Color.FromArgb(255, Mix(baseColor.R, highlight.R), Mix(baseColor.G, highlight.G), Mix(baseColor.B, highlight.B));
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var key = Layout.HitTest(e.GetCurrentPoint(Canvas).Position);
        if (key is null) return;
        Canvas.CapturePointer(e.Pointer);
        _pointerKeys[e.Pointer.PointerId] = key.Value;
        KeyPressed?.Invoke(this, key.Value);
        e.Handled = true;
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerKeys.TryGetValue(e.Pointer.PointerId, out var current)) return;
        var key = Layout.HitTest(e.GetCurrentPoint(Canvas).Position);
        if (key is null || key == current) return;

        // Glissando: slide from key to key.
        KeyReleased?.Invoke(this, current);
        _pointerKeys[e.Pointer.PointerId] = key.Value;
        KeyPressed?.Invoke(this, key.Value);
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerKeys.Remove(e.Pointer.PointerId, out var key))
        {
            KeyReleased?.Invoke(this, key);
        }
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        foreach (var key in _pointerKeys.Values) KeyReleased?.Invoke(this, key);
        _pointerKeys.Clear();
        Canvas.RemoveFromVisualTree();
    }
}
