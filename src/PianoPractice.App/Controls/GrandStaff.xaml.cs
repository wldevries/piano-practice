using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PianoPractice.App.Rendering;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.Controls;

/// <summary>Grand staff showing one chord. Staff size follows the control's height.</summary>
public sealed partial class GrandStaff : UserControl
{
    private readonly GrandStaffRenderer _renderer = new() { ColorForKind = PianoKeyboard.ColorFor };

    public GrandStaff()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Canvas.Invalidate();
    }

    public IReadOnlyList<StaffNote>? Notes
    {
        get => (IReadOnlyList<StaffNote>?)GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    public static readonly DependencyProperty NotesProperty = DependencyProperty.Register(
        nameof(Notes), typeof(IReadOnlyList<StaffNote>), typeof(GrandStaff),
        new PropertyMetadata(null, (d, _) => ((GrandStaff)d).Canvas.Invalidate()));

    public KeySignature Key
    {
        get => (KeySignature)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(KeySignature), typeof(GrandStaff),
        new PropertyMetadata(KeySignature.CMajor, (d, _) => ((GrandStaff)d).Canvas.Invalidate()));

    /// <summary>Lowest MIDI note drawn on the treble staff (default middle C).</summary>
    public int SplitPoint
    {
        get => (int)GetValue(SplitPointProperty);
        set => SetValue(SplitPointProperty, value);
    }

    public static readonly DependencyProperty SplitPointProperty = DependencyProperty.Register(
        nameof(SplitPoint), typeof(int), typeof(GrandStaff),
        new PropertyMetadata(60, (d, _) => ((GrandStaff)d).Canvas.Invalidate()));

    /// <summary>Upper bound for the staff space in DIPs, so the staff doesn't get huge in tall layouts.</summary>
    public double MaxStaffSpace { get; set; } = 16;

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var height = (float)sender.ActualHeight;
        var width = (float)sender.ActualWidth;
        var s = Math.Clamp(height / GrandStaffRenderer.HeightInSpaces, 4f, (float)MaxStaffSpace);

        _renderer.StaffSpace = s;
        _renderer.Ink = ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;

        var left = _renderer.BraceMargin;
        var top = (height - GrandStaffRenderer.HeightInSpaces * s) / 2;
        _renderer.Draw(args.DrawingSession, left, top, width - left - s, Key, Notes ?? [], SplitPoint);
    }

    private void UserControl_ActualThemeChanged(FrameworkElement sender, object args) => Canvas.Invalidate();

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        Canvas.RemoveFromVisualTree();
        _renderer.Dispose();
    }
}
