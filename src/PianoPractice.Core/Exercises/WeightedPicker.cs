namespace PianoPractice.Core.Exercises;

/// <summary>How much practice an item needs, from its stats.</summary>
public static class PracticeWeight
{
    public const double NewItemWeight = 2.5;

    /// <summary>Target answer time; "slow" is never measured against anything faster than this.</summary>
    public const double ComfortableMs = 1500;

    /// <summary>
    /// What counts as a normal answer time for this player: the median of their per-item averages,
    /// but at least <see cref="ComfortableMs"/>. Slowness is relative to it, so a beginner averaging
    /// 6 s still gets the 10 s items singled out, and the bar drops as they get faster.
    /// </summary>
    public static double ReferenceMs(ExerciseStats stats)
    {
        var times = stats.Cards.Values.Where(s => s.Attempts > 0).Select(s => s.AverageMs).Order().ToList();
        if (times.Count == 0) return ComfortableMs;
        var median = times.Count % 2 == 1
            ? times[times.Count / 2]
            : (times[times.Count / 2 - 1] + times[times.Count / 2]) / 2;
        return Math.Max(median, ComfortableMs);
    }

    public static double Of(CardStats? s, double referenceMs = ComfortableMs)
    {
        if (s is null || s.Attempts == 0) return NewItemWeight;
        var inaccuracy = 1 - s.SmoothedAccuracy;                                     // 0..1
        var slowness = Math.Clamp((s.AverageMs - referenceMs) / referenceMs, 0, 2);  // 0..2
        return 1 + 4 * inaccuracy + slowness;
    }
}

/// <summary>
/// Picks the next item, weighted towards items that are new, often wrong or slow.
/// Never repeats the previous item (unless it's the only one).
/// </summary>
public sealed class WeightedPicker<T>(IReadOnlyList<T> items, Func<T, string> idOf, ExerciseStats stats, Random random)
    where T : notnull
{
    public T Next(T? previous)
    {
        if (items.Count == 0) throw new InvalidOperationException("Nothing to pick from.");

        var reference = PracticeWeight.ReferenceMs(stats);
        var candidates = items.Count > 1 && previous is not null
            ? items.Where(i => !EqualityComparer<T>.Default.Equals(i, previous)).ToList()
            : items.ToList();
        var weights = candidates.Select(c => PracticeWeight.Of(stats.For(idOf(c)), reference)).ToArray();

        var roll = random.NextDouble() * weights.Sum();
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0) return candidates[i];
        }
        return candidates[^1];
    }
}
