namespace PianoPractice.Core.Exercises.NoteReading;

/// <summary>
/// Picks the next card, weighted towards cards that are new, often wrong or slow.
/// Never repeats the previous card (unless it's the only one).
/// </summary>
public sealed class CardPicker(IReadOnlyList<NoteCard> deck, NoteReadingStats stats, Random random)
{
    public const double NewCardWeight = 2.5;
    private const double ComfortableMs = 1500;

    public static double Weight(CardStats? s)
    {
        if (s is null || s.Attempts == 0) return NewCardWeight;
        var inaccuracy = 1 - s.SmoothedAccuracy;                                // 0..1
        var slowness = Math.Clamp((s.AverageMs - ComfortableMs) / ComfortableMs, 0, 2); // 0..2
        return 1 + 4 * inaccuracy + slowness;
    }

    public NoteCard Next(NoteCard? previous)
    {
        if (deck.Count == 0) throw new InvalidOperationException("The deck is empty.");

        var candidates = deck.Count > 1 && previous is not null ? deck.Where(c => c != previous).ToList() : deck.ToList();
        var weights = candidates.Select(c => Weight(stats.For(c))).ToArray();

        var roll = random.NextDouble() * weights.Sum();
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0) return candidates[i];
        }
        return candidates[^1];
    }
}
