using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Midi;

public readonly record struct MidiNoteEvent(Pitch Pitch, int Velocity, bool IsNoteOn);

/// <summary>
/// Connection to the piano. Events are raised on a MIDI/background thread;
/// UI consumers must marshal to their dispatcher.
/// </summary>
public interface IMidiService : IDisposable
{
    string? InputName { get; }
    string? OutputName { get; }
    bool IsInputConnected { get; }
    bool IsOutputConnected { get; }

    /// <summary>Last connection error, or null.</summary>
    string? LastError { get; }

    /// <summary>Note on/off received from the piano.</summary>
    event EventHandler<MidiNoteEvent>? NoteReceived;

    /// <summary>Sustain pedal (CC 64) pressed or released.</summary>
    event EventHandler<bool>? SustainChanged;

    /// <summary>Connection state changed, or a device was plugged in or removed.</summary>
    event EventHandler? ConnectionChanged;

    IReadOnlyList<string> GetInputNames();
    IReadOnlyList<string> GetOutputNames();

    /// <summary>Opens the named devices (null = none) and closes any others.</summary>
    void Connect(string? inputName, string? outputName);

    /// <summary>Connects to the preferred devices if present, otherwise to the first device that looks like a piano.</summary>
    void AutoConnect(string? preferredInput, string? preferredOutput);

    void NoteOn(Pitch pitch, int velocity = 80);
    void NoteOff(Pitch pitch);

    /// <summary>Plays a note for a fixed duration on the piano.</summary>
    Task PlayNoteAsync(Pitch pitch, TimeSpan duration, int velocity = 80, CancellationToken cancellationToken = default);

    void AllNotesOff();
}
