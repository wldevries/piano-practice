using PianoPractice.Core.Notation;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Exercises.NoteReading;

/// <summary>One flashcard: a written note on a specific clef.</summary>
public sealed record NoteCard(SpelledPitch Pitch, Clef Clef)
{
    /// <summary>Stable id for statistics, e.g. "Treble:F#4".</summary>
    public string Id => $"{Clef}:{Pitch}";

    public override string ToString() => $"{Pitch} ({Clef.ToString().ToLowerInvariant()})";

    public static NoteCard? TryParseId(string id)
    {
        var parts = id.Split(':');
        if (parts.Length != 2 || !Enum.TryParse<Clef>(parts[0], out var clef)) return null;
        return SpelledPitch.TryParse(parts[1], out var pitch) ? new NoteCard(pitch, clef) : null;
    }
}

/// <summary>Builds the set of cards an options selection allows.</summary>
public static class NoteReadingDeck
{
    public static IReadOnlyList<NoteCard> Build(NoteReadingOptions options)
    {
        var key = new KeySignature(options.KeyFifths);
        var clefs = options.Clefs switch
        {
            ClefChoice.Treble => [Clef.Treble],
            ClefChoice.Bass => [Clef.Bass],
            _ => new[] { Clef.Treble, Clef.Bass },
        };
        var ledger = Math.Clamp(options.LedgerLines, 0, 4);

        var cards = new List<NoteCard>();
        foreach (var clef in clefs)
        {
            for (var step = StaffPosition.BottomLine - 2 * ledger; step <= StaffPosition.TopLine + 2 * ledger; step++)
            {
                var natural = StaffPosition.PitchAt(step, clef);
                var inKey = key.AccidentalFor(natural.Letter);
                var accidentals = options.IncludeAccidentals
                    ? new[] { inKey - 1, inKey, inKey + 1 }.Where(a => a is >= -1 and <= 1)
                    : [inKey];

                foreach (var accidental in accidentals)
                {
                    var pitch = natural with { Accidental = accidental };
                    if (pitch.Pitch.IsOnPiano) cards.Add(new NoteCard(pitch, clef));
                }
            }
        }
        return cards;
    }
}
