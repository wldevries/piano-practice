using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PianoPractice.App.Rendering;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.Pages;

public sealed partial class LivePage : Page
{
    // M0 demo content: a scale fragment with ledger lines and accidentals.
    private static readonly SpelledPitch[] DemoNotes =
    [
        new(Letter.C, 0, 4),
        new(Letter.E, 0, 4),
        new(Letter.G, 0, 4),
        new(Letter.B, -1, 4),
        new(Letter.C, 0, 5),
        new(Letter.F, 1, 5),
        new(Letter.A, 0, 5),
        new(Letter.C, 0, 6),
    ];

    private readonly StaffRenderer _staff = new() { StaffSpace = 18f };

    public LivePage()
    {
        InitializeComponent();
    }

    private void StaffCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        _staff.Ink = ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        _staff.DrawTrebleStaff(args.DrawingSession, new Vector2(8, 4 * _staff.StaffSpace), 40 * _staff.StaffSpace, DemoNotes);
    }

    private void Page_ActualThemeChanged(FrameworkElement sender, object args) => StaffCanvas.Invalidate();

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Win2D recommendation: release device resources when the control leaves the tree.
        StaffCanvas.RemoveFromVisualTree();
    }
}
