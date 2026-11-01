using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Midi;

/// <summary>
/// MIDI via DryWetMIDI (WinMM). Hot-plug is handled by polling the device list:
/// DryWetMIDI's DevicesWatcher doesn't fire on this setup (Windows MIDI Services), and an
/// open handle silently goes dead after an unplug, so the poll closes vanished devices
/// and reopens them when they come back.
/// </summary>
public sealed class DryWetMidiService : IMidiService
{
    private const int SustainController = 64;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Which device to use: a specific name, or (Auto) the preferred name if present, else any piano.</summary>
    private readonly record struct DeviceChoice(string? Name, bool Auto)
    {
        public string? Resolve(IReadOnlyList<string> available) => Auto
            ? (Name is not null && available.Contains(Name) ? Name : available.FirstOrDefault(LooksLikePiano))
            : (Name is not null && available.Contains(Name) ? Name : null);
    }

    private readonly Lock _gate = new();
    private readonly Timer _pollTimer;
    private int _polling;

    private InputDevice? _input;
    private OutputDevice? _output;
    private DeviceChoice _inputChoice = new(null, Auto: true);
    private DeviceChoice _outputChoice = new(null, Auto: true);
    private IReadOnlyList<string> _lastInputs = [];
    private IReadOnlyList<string> _lastOutputs = [];
    private bool _disposed;

    public DryWetMidiService()
    {
        _pollTimer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
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

    public void AutoConnect(string? preferredInput, string? preferredOutput) =>
        Apply(new DeviceChoice(preferredInput, Auto: true), new DeviceChoice(preferredOutput, Auto: true));

    public void Connect(string? inputName, string? outputName) =>
        Apply(new DeviceChoice(inputName, Auto: false), new DeviceChoice(outputName, Auto: false));

    private void Apply(DeviceChoice input, DeviceChoice output)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _inputChoice = input;
            _outputChoice = output;
            CloseInput();
            CloseOutput();
            LastError = null;
        }
        Reconcile(forceNotify: true);
    }

    private void Poll()
    {
        // Skip a tick if the previous one is still running.
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            Reconcile(forceNotify: false);
        }
        catch (Exception)
        {
            // Never let a timer callback take the process down; try again next tick.
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    /// <summary>Brings open devices in line with what's plugged in and what was chosen.</summary>
    private void Reconcile(bool forceNotify)
    {
        var inputs = GetInputNames();
        var outputs = GetOutputNames();
        var changed = forceNotify;

        lock (_gate)
        {
            if (_disposed) return;

            if (!inputs.SequenceEqual(_lastInputs) || !outputs.SequenceEqual(_lastOutputs))
            {
                _lastInputs = inputs;
                _lastOutputs = outputs;
                changed = true;
            }

            // Unplugged: the handle is dead even if the device comes back, so close it.
            if (_input is not null && !inputs.Contains(InputName!))
            {
                CloseInput();
                changed = true;
            }
            if (_output is not null && !outputs.Contains(OutputName!))
            {
                CloseOutput();
                changed = true;
            }

            if (_input is null && _inputChoice.Resolve(inputs) is { } inputName)
            {
                changed |= TryOpenInput(inputName);
            }
            if (_output is null && _outputChoice.Resolve(outputs) is { } outputName)
            {
                changed |= TryOpenOutput(outputName);
            }
        }

        if (changed) ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryOpenInput(string name)
    {
        try
        {
            _input = InputDevice.GetByName(name);
            _input.EventReceived += OnEventReceived;
            _input.StartEventsListening();
            InputName = name;
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is MidiDeviceException or ArgumentException or InvalidOperationException)
        {
            _input?.Dispose();
            _input = null;
            var error = $"Could not open input '{name}': {ex.Message}";
            var isNew = error != LastError;
            LastError = error;
            return isNew; // notify once, not on every retry
        }
    }

    private bool TryOpenOutput(string name)
    {
        try
        {
            _output = OutputDevice.GetByName(name);
            _output.PrepareForEventsSending();
            OutputName = name;
            return true;
        }
        catch (Exception ex) when (ex is MidiDeviceException or ArgumentException or InvalidOperationException)
        {
            _output?.Dispose();
            _output = null;
            var error = $"Could not open output '{name}': {ex.Message}";
            var isNew = error != LastError;
            LastError = error;
            return isNew;
        }
    }

    private void CloseInput()
    {
        if (_input is null) return;
        _input.EventReceived -= OnEventReceived;
        try { _input.StopEventsListening(); } catch (MidiDeviceException) { /* already gone */ }
        _input.Dispose();
        _input = null;
        InputName = null;
    }

    private void CloseOutput()
    {
        if (_output is null) return;
        try { _output.TurnAllNotesOff(); } catch (MidiDeviceException) { /* already gone */ }
        _output.Dispose();
        _output = null;
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
            try { _output?.SendEvent(midiEvent); } catch (MidiDeviceException) { /* unplugged; the poll will notice */ }
        }
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CloseInput();
            CloseOutput();
        }
    }
}
