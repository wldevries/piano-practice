using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PianoPractice.Core.Midi;
using PianoPractice.Core.Settings;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public const string None = "(none)";

    private readonly IMidiService _midi;
    private readonly SettingsStore _settings;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool _refreshing;

    public SettingsViewModel(IMidiService midi, SettingsStore settings)
    {
        _midi = midi;
        _settings = settings;
        _midi.ConnectionChanged += OnConnectionChanged;
        Refresh();
    }

    public ObservableCollection<string> Inputs { get; } = [];
    public ObservableCollection<string> Outputs { get; } = [];

    [ObservableProperty]
    public partial string? SelectedInput { get; set; }

    [ObservableProperty]
    public partial string? SelectedOutput { get; set; }

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    partial void OnSelectedInputChanged(string? value) => ApplySelection();
    partial void OnSelectedOutputChanged(string? value) => ApplySelection();

    private void OnConnectionChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Refresh);

    [RelayCommand]
    private void Refresh()
    {
        _refreshing = true;
        try
        {
            Sync(Inputs, _midi.GetInputNames());
            Sync(Outputs, _midi.GetOutputNames());
            SelectedInput = _midi.InputName ?? None;
            SelectedOutput = _midi.OutputName ?? None;
            ErrorText = _midi.LastError;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static void Sync(ObservableCollection<string> target, IReadOnlyList<string> names)
    {
        var wanted = names.Prepend(None).ToList();
        if (target.SequenceEqual(wanted)) return;
        target.Clear();
        foreach (var name in wanted) target.Add(name);
    }

    private void ApplySelection()
    {
        if (_refreshing || SelectedInput is null || SelectedOutput is null) return;

        var input = SelectedInput == None ? null : SelectedInput;
        var output = SelectedOutput == None ? null : SelectedOutput;
        if (input == _midi.InputName && output == _midi.OutputName) return;

        _midi.Connect(input, output);
        _settings.Update(s => s with { MidiInput = input, MidiOutput = output, MidiChosenManually = true });
    }

    /// <summary>Plays a short C major arpeggio on the output to check the connection.</summary>
    [RelayCommand]
    private async Task TestSoundAsync()
    {
        foreach (var midi in new[] { 60, 64, 67, 72 })
        {
            await _midi.PlayNoteAsync(new Pitch(midi), TimeSpan.FromMilliseconds(220));
        }
    }

    public void Dispose() => _midi.ConnectionChanged -= OnConnectionChanged;
}
