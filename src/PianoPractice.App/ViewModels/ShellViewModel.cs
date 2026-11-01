using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using PianoPractice.Core.Midi;

namespace PianoPractice.App.ViewModels;

/// <summary>Connection status shown in the navigation pane footer.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IMidiService _midi;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public ShellViewModel(IMidiService midi)
    {
        _midi = midi;
        _midi.ConnectionChanged += (_, _) => _dispatcher.TryEnqueue(Update);
        Update();
    }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    private void Update()
    {
        IsConnected = _midi.IsInputConnected;
        StatusText = _midi.InputName ?? "No piano connected";
    }
}
