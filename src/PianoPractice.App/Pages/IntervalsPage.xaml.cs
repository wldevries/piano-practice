using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PianoPractice.App.ViewModels;

namespace PianoPractice.App.Pages;

public sealed partial class IntervalsPage : Page
{
    public IntervalsPage()
    {
        InitializeComponent();
    }

    public IntervalsViewModel ViewModel { get; } = App.GetService<IntervalsViewModel>();

    private void NameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int semitones }) ViewModel.AnswerName(semitones);
    }

    private void Keyboard_KeyPressed(object? sender, int midi) => ViewModel.ScreenKeyDown(midi);
    private void Keyboard_KeyReleased(object? sender, int midi) => ViewModel.ScreenKeyUp(midi);

    private void Page_Unloaded(object sender, RoutedEventArgs e) => ViewModel.Dispose();
}
