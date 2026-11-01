namespace PianoPractice.Core.Theory;

public sealed record ChordResult(int RootPitchClass, string Suffix, int BassPitchClass)
{
    public bool IsInverted => RootPitchClass != BassPitchClass;

    /// <summary>Chord symbol, e.g. "C", "Am7", "G7/B".</summary>
    public string Name => NoteNames.PitchClass(RootPitchClass) + Suffix
        + (IsInverted ? "/" + NoteNames.PitchClass(BassPitchClass) : "");

    public override string ToString() => Name;
}

/// <summary>
/// Names a set of held notes as a chord symbol. Only pitch classes matter (octave doublings
/// are ignored); the lowest note decides the bass and is preferred as the root.
/// </summary>
public static class ChordDetector
{
    // Interval sets relative to the root, in priority order. Earlier entries win when
    // a note set can be read more than one way and the bass doesn't settle it.
    private static readonly (int[] Intervals, string Suffix)[] Templates =
    [
        ([0, 4, 7], ""),
        ([0, 3, 7], "m"),
        ([0, 4, 7, 10], "7"),
        ([0, 3, 7, 10], "m7"),
        ([0, 4, 7, 11], "maj7"),
        ([0, 3, 6], "dim"),
        ([0, 4, 8], "aug"),
        ([0, 5, 7], "sus4"),
        ([0, 2, 7], "sus2"),
        ([0, 3, 6, 9], "dim7"),
        ([0, 3, 6, 10], "m7b5"),
        ([0, 4, 7, 9], "6"),
        ([0, 3, 7, 9], "m6"),
        ([0, 3, 7, 11], "m(maj7)"),
        ([0, 5, 7, 10], "7sus4"),
        ([0, 2, 4, 7], "add9"),
        ([0, 2, 3, 7], "m(add9)"),
        ([0, 2, 4, 7, 10], "9"),
        ([0, 2, 4, 7, 11], "maj9"),
        ([0, 2, 3, 7, 10], "m9"),
        // Common voicings with the fifth left out.
        ([0, 4, 10], "7"),
        ([0, 4, 11], "maj7"),
        ([0, 3, 10], "m7"),
        ([0, 7], "5"),
    ];

    public static ChordResult? Detect(IEnumerable<Pitch> pitches)
    {
        var sorted = pitches.Distinct().Order().ToList();
        if (sorted.Count < 2) return null;

        var bass = sorted[0].PitchClass;
        var pitchClasses = sorted.Select(p => p.PitchClass).Distinct().ToList();
        if (pitchClasses.Count < 2) return null;

        // 1) Root in the bass: any template that fits wins.
        foreach (var (intervals, suffix) in Templates)
        {
            if (Matches(pitchClasses, bass, intervals)) return new ChordResult(bass, suffix, bass);
        }

        // 2) Inversions: template priority first, then candidate roots from low to high.
        var candidates = sorted.Select(p => p.PitchClass).Distinct().Where(pc => pc != bass).ToList();
        foreach (var (intervals, suffix) in Templates)
        {
            foreach (var root in candidates)
            {
                if (Matches(pitchClasses, root, intervals)) return new ChordResult(root, suffix, bass);
            }
        }

        return null;
    }

    private static bool Matches(List<int> pitchClasses, int root, int[] intervals)
    {
        if (pitchClasses.Count != intervals.Length) return false;
        foreach (var pc in pitchClasses)
        {
            if (Array.IndexOf(intervals, ((pc - root) % 12 + 12) % 12) < 0) return false;
        }
        return true;
    }
}
