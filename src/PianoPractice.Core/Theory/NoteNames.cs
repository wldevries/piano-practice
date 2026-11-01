namespace PianoPractice.Core.Theory;

/// <summary>Context-free display names, using the spellings most common in chord symbols.</summary>
public static class NoteNames
{
    private static readonly string[] PitchClassNames = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    private static readonly string[] IntervalNames = ["P1", "m2", "M2", "m3", "M3", "P4", "TT", "P5", "m6", "M6", "m7", "M7"];

    public static string PitchClass(int pitchClass) => PitchClassNames[((pitchClass % 12) + 12) % 12];

    /// <summary>Name with octave, e.g. "Eb4".</summary>
    public static string WithOctave(Pitch pitch) => $"{PitchClass(pitch.PitchClass)}{pitch.Octave}";

    /// <summary>Short interval name for a distance in semitones, e.g. 7 → "P5", 16 → "M3 +8va".</summary>
    public static string Interval(int semitones)
    {
        semitones = Math.Abs(semitones);
        if (semitones == 0) return "P1";
        if (semitones % 12 == 0) return semitones == 12 ? "P8" : $"P8 +{semitones / 12 - 1}×8va";
        var octaves = semitones / 12;
        var name = IntervalNames[semitones % 12];
        return octaves == 0 ? name : $"{name} +{(octaves == 1 ? "" : octaves.ToString() + "×")}8va";
    }
}
