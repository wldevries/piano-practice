using Microsoft.UI.Xaml;

namespace PianoPractice.App;

public partial class App : Application
{
    private Window? _window;

    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PianoPractice");

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => LogCrash(e.Exception);
    }

    private static void LogCrash(Exception ex)
    {
        Directory.CreateDirectory(DataFolder);
        File.AppendAllText(Path.Combine(DataFolder, "crash.log"), $"[{DateTime.Now:O}] {ex}\n\n");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
