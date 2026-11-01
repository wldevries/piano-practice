using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Midi;

public sealed class DryWetMidiService : IMidiService
{
    private const int SustainController = 64;

    private readonly Lock _gate = new();
    private InputDevice? _input;
    private OutputDevice? _output;

    // What the user asked for; used to reconnect after unplug/replug.
    private string? _wantedInput;
    private string? _wantedOutput;
    private bool _disposed;

    public DryWetMidiService()
    {
        try
        {
            DevicesWatcher.Instance.DeviceAdded += OnDeviceAdded;
            DevicesWatcher.Instance.DeviceRemoved += OnDeviceRemoved;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or MidiDeviceException)
        {
            // Hot-plug detection is a nicety; the Settings page can still refresh manually.
        }
    }

    public string? InputName { get; private set; }
    public string? OutputName { get; private set; }
    public bool IsInputConnected => _input is not null;
    public bool IsOutputConnected => _output is not null;
    public string? LastError { get; private set; }

    public event EventHandler<MidiNoteEvent>? NoteReceived;
    public event EventHandler<bool>? SustainChanged;
    public event EventHandler? ConnectionChanged;

    public IReadOnlyList<string> GetInputNames() => ListNames(InputDevice.GetAll());
    public IReadOnlyList<string> GetOutputNames() => ListNames(OutputDevice.GetAll());

    private static List<string> ListNames<T>(ICollection<T> devices) where T : MidiDevice
    {
        var names = devices.Select(d => d.Name).ToList();
        foreach (var d in devices) d.Dispose();
        return names;
    }

    /// <summary>
    /// Heuristic for "this is a digital piano". The FP-10 shows up as "Roland Digital Piano" over USB;
    /// "BT-MIDI" is the default port name of the Perfect-Bluetooth-MIDI-For-Windows bridge.
    /// </summary>
    public static bool LooksLikePiano(string deviceName) =>
        deviceName.Contains("Piano", StringComparison.OrdinalIgnoreCase)
        || deviceName.Contains("Roland", StringComparison.OrdinalIgnoreCase)
        || deviceName.Contains("FP-", StringComparison.OrdinalIgnoreCase)
        || deviceName.Contains("BT-MIDI", StringComparison.OrdinalIgnoreCase);

    public void AutoConnect(string? preferredInput, string? preferredOutput)
    {
        Connect(
            Pick(GetInputNames(), preferredInput),
            Pick(GetOutputNames(), preferredOutput));
    }

    private static string? Pick(IReadOnlyList<string> available, string? preferred) =>
        preferred is not null && available.Contains(preferred)
            ? preferred
            : available.FirstOrDefault(LooksLikePiano);

    public void Connect(string? inputName, string? outputName)
    {
        lock (_gate)
        {
            _wantedInput = inputName;
            _wantedOutput = outputName;
            CloseDevices();
            LastError = null;

            if (inputName is not null)
            {
                try
                {
                    _input = InputDevice.GetByName(inputName);
                    _input.EventReceived += OnEventReceived;
                    _input.StartEventsListening();
                    InputName = inputName;
                }
                catch (Exception ex) when (ex is MidiDeviceException or ArgumentException or InvalidOperationException)
                {
                    LastError = $"Could not open input '{inputName}': {ex.Message}";
                    _input?.Dispose();
                    _input = null;
                }
            }

            if (outputName is not null)
            {
                try
                {
                    _output = OutputDevice.GetByName(outputName);
                    _output.PrepareForEventsSending();
                    OutputName = outputName;
                }
                catch (Exception ex) when (ex is MidiDeviceException or ArgumentException or InvalidOperationException)
                {
                    LastError = (LastError is null ? "" : LastError + " ") + $"Could not open output '{outputName}': {ex.Message}";
                    _output?.Dispose();
                    _output = null;
                }
            }
        }
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseDevices()
    {
        if (_input is not null)
        {
            _input.EventReceived -= OnEventReceived;
            try { _input.StopEventsListening(); } catch (MidiDeviceException) { /* already gone */ }
            _input.Dispose();
            _input = null;
        }
        if (_output is not null)
        {
            try { _output.TurnAllNotesOff(); } catch (MidiDeviceException) { /* already gone */ }
            _output.Dispose();
            _output = null;
        }
        InputName = null;
        OutputName = null;
    }

    private void OnEventReceived(object? sender, MidiEventReceivedEventArgs e)
    {
        switch (e.Event)
        {
            case NoteOnEvent on when on.Velocity > 0:
                NoteReceived?.Invoke(this, new MidiNoteEvent(new Pitch(on.NoteNumber), on.Velocity, true));
                break;
            case NoteOnEvent on: // velocity 0 = note off
                NoteReceived?.Invoke(this, new MidiNoteEvent(new Pitch(on.NoteNumber), 0, false));
                break;
            case NoteOffEvent off:
                NoteReceived?.Invoke(this, new MidiNoteEvent(new Pitch(off.NoteNumber), off.Velocity, false));
                break;
            case ControlChangeEvent cc when cc.ControlNumber == SustainController:
                SustainChanged?.Invoke(this, cc.ControlValue >= 64);
                break;
        }
    }

    private void OnDeviceAdded(object? sender, DeviceAddedRemovedEventArgs e)
    {
        bool reconnect;
        lock (_gate)
        {
            // Reconnect when a device we want (or any piano, if we have nothing) comes back.
            reconnect = !_disposed && (
                (_input is null && (e.Device.Name == _wantedInput || (_wantedInput is null && LooksLikePiano(e.Device.Name))))
                || (_output is null && (e.Device.Name == _wantedOutput || (_wantedOutput is null && LooksLikePiano(e.Device.Name)))));
        }
        if (reconnect)
        {
            AutoConnect(_wantedInput ?? InputName, _wantedOutput ?? OutputName);
        }
        else
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDeviceRemoved(object? sender, DeviceAddedRemovedEventArgs e)
    {
        lock (_gate)
        {
            if (e.Device.Name == InputName && _input is not null)
            {
                _input.EventReceived -= OnEventReceived;
                _input.Dispose();
                _input = null;
                InputName = null;
            }
            if (e.Device.Name == OutputName && _output is not null)
            {
                _output.Dispose();
                _output = null;
                OutputName = null;
            }
        }
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NoteOn(Pitch pitch, int velocity = 80) =>
        Send(new NoteOnEvent((SevenBitNumber)pitch.MidiNumber, (SevenBitNumber)Math.Clamp(velocity, 1, 127)));

    public void NoteOff(Pitch pitch) =>
        Send(new NoteOffEvent((SevenBitNumber)pitch.MidiNumber, (SevenBitNumber)0));

    public async Task PlayNoteAsync(Pitch pitch, TimeSpan duration, int velocity = 80, CancellationToken cancellationToken = default)
    {
        NoteOn(pitch, velocity);
        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        finally
        {
            NoteOff(pitch);
        }
    }

    public void AllNotesOff()
    {
        lock (_gate)
        {
            try { _output?.TurnAllNotesOff(); } catch (MidiDeviceException) { /* device gone */ }
        }
    }

    private void Send(MidiEvent midiEvent)
    {
        lock (_gate)
        {
            try { _output?.SendEvent(midiEvent); } catch (MidiDeviceException) { /* device gone; removal event will follow */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                DevicesWatcher.Instance.DeviceAdded -= OnDeviceAdded;
                DevicesWatcher.Instance.DeviceRemoved -= OnDeviceRemoved;
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or MidiDeviceException) { }
            CloseDevices();
        }
    }
}
