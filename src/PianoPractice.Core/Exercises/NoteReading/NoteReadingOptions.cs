namespace PianoPractice.Core.Exercises.NoteReading;

public enum ClefChoice { Treble, Bass, Both }

public sealed record NoteReadingOptions
{
    public ClefChoice Clefs { get; init; } = ClefChoice.Treble;

    /// <summary>How many ledger lines above/below the staff notes may use (0 = only on the staff).</summary>
    public int LedgerLines { get; init; }

    /// <summary>Also ask notes with an accidental relative to the key (sharps, flats, naturals).</summary>
    public bool IncludeAccidentals { get; init; }

    /// <summary>Key signature in fifths (-7..7).</summary>
    public int KeyFifths { get; init; }

    /// <summary>When false, any octave of the right note counts.</summary>
    public bool RequireExactOctave { get; init; } = true;

    public int CardsPerSession { get; init; } = 20;

    public string Describe()
    {
        var parts = new List<string>
        {
            Clefs switch { ClefChoice.Treble => "Treble", ClefChoice.Bass => "Bass", _ => "Both clefs" },
            LedgerLines == 0 ? "staff only" : $"±{LedgerLines} ledger line{(LedgerLines == 1 ? "" : "s")}",
        };
        if (IncludeAccidentals) parts.Add("accidentals");
        if (KeyFifths != 0) parts.Add(new Theory.KeySignature(KeyFifths).MajorName + " major");
        if (!RequireExactOctave) parts.Add("any octave");
        return string.Join(", ", parts);
    }
}
