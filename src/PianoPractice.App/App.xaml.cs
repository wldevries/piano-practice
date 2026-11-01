using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using PianoPractice.App.ViewModels;
using PianoPractice.Core.Midi;
using PianoPractice.Core.Settings;

namespace PianoPractice.App;

public partial class App : Application
{
    private Window? _window;

    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PianoPractice");

    public static IServiceProvider Services { get; } = ConfigureServices();

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => LogCrash(e.Exception);
    }

    private static IServiceProvider ConfigureServices() => new ServiceCollection()
        .AddSingleton(new SettingsStore(DataFolder))
        .AddSingleton<IMidiService, DryWetMidiService>()
        .AddSingleton<ShellViewModel>()
        .AddTransient<LiveViewModel>()
        .AddTransient<SettingsViewModel>()
        .BuildServiceProvider();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = GetService<SettingsStore>().Current;
        var midi = GetService<IMidiService>();
        midi.AutoConnect(settings.MidiInput, settings.MidiOutput);

        _window = new MainWindow();
        _window.Closed += (_, _) => midi.Dispose();
        _window.Activate();
    }

    private static void LogCrash(Exception ex)
    {
        Directory.CreateDirectory(DataFolder);
        File.AppendAllText(Path.Combine(DataFolder, "crash.log"), $"[{DateTime.Now:O}] {ex}\n\n");
    }
}
