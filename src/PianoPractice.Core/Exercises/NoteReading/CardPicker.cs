namespace PianoPractice.Core.Exercises.NoteReading;

/// <summary>
/// Picks the next card, weighted towards cards that are new, often wrong or slow.
/// Never repeats the previous card (unless it's the only one).
/// </summary>
public sealed class CardPicker(IReadOnlyList<NoteCard> deck, NoteReadingStats stats, Random random)
{
    public const double NewCardWeight = 2.5;

    /// <summary>Target answer time; "slow" is never measured against anything faster than this.</summary>
    public const double ComfortableMs = 1500;

    /// <summary>
    /// What counts as a normal answer time for this player: the median of their per-card averages,
    /// but at least <see cref="ComfortableMs"/>. Slowness is relative to it, so a beginner averaging
    /// 6 s still gets the 10 s notes singled out, and the bar drops as they get faster.
    /// </summary>
    public static double ReferenceMs(NoteReadingStats stats)
    {
        var times = stats.Cards.Values.Where(s => s.Attempts > 0).Select(s => s.AverageMs).Order().ToList();
        if (times.Count == 0) return ComfortableMs;
        var median = times.Count % 2 == 1
            ? times[times.Count / 2]
            : (times[times.Count / 2 - 1] + times[times.Count / 2]) / 2;
        return Math.Max(median, ComfortableMs);
    }

    public static double Weight(CardStats? s, double referenceMs = ComfortableMs)
    {
        if (s is null || s.Attempts == 0) return NewCardWeight;
        var inaccuracy = 1 - s.SmoothedAccuracy;                                     // 0..1
        var slowness = Math.Clamp((s.AverageMs - referenceMs) / referenceMs, 0, 2);  // 0..2
        return 1 + 4 * inaccuracy + slowness;
    }

    public NoteCard Next(NoteCard? previous)
    {
        if (deck.Count == 0) throw new InvalidOperationException("The deck is empty.");

        var reference = ReferenceMs(stats);
        var candidates = deck.Count > 1 && previous is not null ? deck.Where(c => c != previous).ToList() : deck.ToList();
        var weights = candidates.Select(c => Weight(stats.For(c), reference)).ToArray();

        var roll = random.NextDouble() * weights.Sum();
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0) return candidates[i];
        }
        return candidates[^1];
    }
}
