using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PianoPractice.App.ViewModels;

namespace PianoPractice.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; } = App.GetService<SettingsViewModel>();

    // x:Bind function bindings need instance methods.
    public bool HasText(string? text) => !string.IsNullOrEmpty(text);

    private void Page_Unloaded(object sender, RoutedEventArgs e) => ViewModel.Dispose();
}
