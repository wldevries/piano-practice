namespace PianoPractice.Core.Theory;

/// <summary>
/// A sounding pitch, identified by its MIDI note number (60 = middle C = C4).
/// Spelling (C# vs Db) is a separate concern handled by <see cref="SpelledPitch"/>.
/// </summary>
public readonly record struct Pitch(int MidiNumber) : IComparable<Pitch>
{
    public const int PianoLowest = 21;   // A0
    public const int PianoHighest = 108; // C8

    public static Pitch MiddleC => new(60);

    /// <summary>Pitch class 0..11, where 0 = C.</summary>
    public int PitchClass => ((MidiNumber % 12) + 12) % 12;

    /// <summary>Scientific pitch notation octave: C4 = middle C.</summary>
    public int Octave => (MidiNumber / 12) - 1;

    /// <summary>True for the black keys on a piano.</summary>
    public bool IsBlackKey => PitchClass is 1 or 3 or 6 or 8 or 10;

    public bool IsOnPiano => MidiNumber is >= PianoLowest and <= PianoHighest;

    public Pitch Transpose(int semitones) => new(MidiNumber + semitones);

    public int CompareTo(Pitch other) => MidiNumber.CompareTo(other.MidiNumber);

    /// <summary>Default spelling, using sharps for black keys.</summary>
    public override string ToString() => SpelledPitch.FromPitch(this).ToString();
}
