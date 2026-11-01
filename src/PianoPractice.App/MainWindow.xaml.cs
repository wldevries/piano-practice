using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PianoPractice.App.Pages;
using PianoPractice.App.ViewModels;
using Windows.Graphics;

namespace PianoPractice.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> PagesByTag = new()
    {
        ["Live"] = typeof(LivePage),
        ["NoteReading"] = typeof(NoteReadingPage),
        ["Intervals"] = typeof(IntervalsPage),
        ["PlayAlong"] = typeof(PlayAlongPage),
        ["Settings"] = typeof(SettingsPage),
    };

    private static readonly SolidColorBrush ConnectedBrush = new(Colors.LimeGreen);
    private static readonly SolidColorBrush DisconnectedBrush = new(Colors.Gray);

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1400, 900));
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    public ShellViewModel Shell { get; } = App.GetService<ShellViewModel>();

    // x:Bind function bindings need instance methods.
    public Brush StatusBrush(bool connected) => connected ? ConnectedBrush : DisconnectedBrush;

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "Settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag is not null && PagesByTag.TryGetValue(tag, out var pageType) && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
        }
    }
}
