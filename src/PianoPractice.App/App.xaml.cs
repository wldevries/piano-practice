using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using PianoPractice.App.ViewModels;
using PianoPractice.Core.Exercises;
using PianoPractice.Core.Exercises.Intervals;
using PianoPractice.Core.Exercises.NoteReading;
using PianoPractice.Core.Midi;
using PianoPractice.Core.Settings;

namespace PianoPractice.App;

public partial class App : Application
{
    private Window? _window;

    /// <summary>The main window, for APIs that need a window handle (file pickers).</summary>
    public static Window? CurrentWindow { get; private set; }

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
        .AddKeyedSingleton(StatsKeys.NoteReading, new ExerciseStatsStore(DataFolder, NoteReadingSession.StatsFileName))
        .AddKeyedSingleton(StatsKeys.Intervals, new ExerciseStatsStore(DataFolder, IntervalSession.StatsFileName))
        .AddKeyedSingleton(StatsKeys.PlayAlong, new ExerciseStatsStore(DataFolder, "play-along-stats.json"))
        .AddSingleton<ShellViewModel>()
        .AddTransient<LiveViewModel>()
        .AddTransient<NoteReadingViewModel>()
        .AddTransient<IntervalsViewModel>()
        .AddTransient<PlayAlongViewModel>()
        .AddTransient<SettingsViewModel>()
        .BuildServiceProvider();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = GetService<SettingsStore>().Current;
        var midi = GetService<IMidiService>();
        if (settings.MidiChosenManually) midi.Connect(settings.MidiInput, settings.MidiOutput);
        else midi.AutoConnect(settings.MidiInput, settings.MidiOutput);

        _window = CurrentWindow = new MainWindow();
        _window.Closed += (_, _) => midi.Dispose();
        _window.Activate();
    }

    private static void LogCrash(Exception ex)
    {
        Directory.CreateDirectory(DataFolder);
        File.AppendAllText(Path.Combine(DataFolder, "crash.log"), $"[{DateTime.Now:O}] {ex}\n\n");
    }
}
