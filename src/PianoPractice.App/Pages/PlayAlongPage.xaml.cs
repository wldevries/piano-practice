using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PianoPractice.App.ViewModels;
using Windows.Storage.Pickers;

namespace PianoPractice.App.Pages;

public sealed partial class PlayAlongPage : Page
{
    private readonly Stopwatch _clock = new();
    private TimeSpan _lastFrame;

    public PlayAlongPage()
    {
        InitializeComponent();
    }

    public PlayAlongViewModel ViewModel { get; } = App.GetService<PlayAlongViewModel>();

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Notes.ShowNoteNames = ViewModel.ShowNoteNames;
        Notes.Engine = Sheet.Engine = ViewModel.Engine;
        RefreshView();

        _clock.Restart();
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Dispose();
    }

    /// <summary>Once per display frame: advance the song and redraw the falling notes.</summary>
    private void OnRendering(object? sender, object e)
    {
        var now = _clock.Elapsed;
        var dt = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        if (!ViewModel.IsPlaying) return;
        ViewModel.Tick(dt);
        RefreshView();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayAlongViewModel.Engine):
                Notes.Engine = Sheet.Engine = ViewModel.Engine;
                RefreshView();
                break;
            case nameof(PlayAlongViewModel.ShowNoteNames):
                Notes.ShowNoteNames = ViewModel.ShowNoteNames;
                RefreshView();
                break;
            case nameof(PlayAlongViewModel.IsPlaying):
            case nameof(PlayAlongViewModel.TempoPercent):
            case nameof(PlayAlongViewModel.ViewIndex):
                RefreshView();
                break;
        }
    }

    private void RefreshView()
    {
        if (ViewModel.IsSheetMusic) Sheet.Refresh();
        else Notes.Refresh();
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".mid");
        picker.FileTypeFilter.Add(".midi");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow));

        var file = await picker.PickSingleFileAsync();
        if (file is not null) ViewModel.OpenFile(file.Path);
    }

    private void PlayPause_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTyping()) return;
        ViewModel.PlayPauseCommand.Execute(null);
        args.Handled = true;
    }

    private void Restart_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTyping()) return;
        ViewModel.RestartCommand.Execute(null);
        RefreshView();
        args.Handled = true;
    }

    /// <summary>Don't steal Space/R from a number box being edited.</summary>
    private bool IsTyping() => FocusManager.GetFocusedElement(XamlRoot) is TextBox;

    private void Keyboard_KeyPressed(object? sender, int midi) => ViewModel.ScreenKeyDown(midi);
    private void Keyboard_KeyReleased(object? sender, int midi) => ViewModel.ScreenKeyUp(midi);
}
