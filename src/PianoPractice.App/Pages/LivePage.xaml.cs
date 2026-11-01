using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PianoPractice.App.ViewModels;

namespace PianoPractice.App.Pages;

public sealed partial class LivePage : Page
{
    public LivePage()
    {
        InitializeComponent();
    }

    public LiveViewModel ViewModel { get; } = App.GetService<LiveViewModel>();

    private void Keyboard_KeyPressed(object? sender, int midi) => ViewModel.ScreenKeyDown(midi);
    private void Keyboard_KeyReleased(object? sender, int midi) => ViewModel.ScreenKeyUp(midi);

    private void Page_Unloaded(object sender, RoutedEventArgs e) => ViewModel.Dispose();
}
