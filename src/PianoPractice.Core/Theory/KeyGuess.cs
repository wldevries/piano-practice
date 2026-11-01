namespace PianoPractice.Core.Theory;

/// <summary>Guesses a key signature from the notes, for MIDI files that don't say.</summary>
public static class KeyGuess
{
    private static readonly int[] MajorScale = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>
    /// The key signature whose scale covers the most note-time; ties go to fewer sharps/flats.
    /// Relative minors share a signature, so A minor comes out as C major (the same signature).
    /// </summary>
    public static KeySignature FromNotes(IEnumerable<(Pitch Pitch, double Duration)> notes)
    {
        var weight = new double[12];
        foreach (var (pitch, duration) in notes) weight[pitch.PitchClass] += Math.Max(duration, 0.05);
        if (weight.Sum() == 0) return KeySignature.CMajor;

        return Enumerable.Range(-6, 13) // up to 6 sharps/flats; 7 is rarely what a file means
            .Select(fifths =>
            {
                var tonic = ((7 * fifths) % 12 + 12) % 12;
                var inScale = MajorScale.Sum(step => weight[(tonic + step) % 12]);
                return (Fifths: fifths, Score: inScale);
            })
            .OrderByDescending(k => Math.Round(k.Score, 6))
            .ThenBy(k => Math.Abs(k.Fifths))
            .Select(k => new KeySignature(k.Fifths))
            .First();
    }
}
