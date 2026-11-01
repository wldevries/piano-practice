namespace PianoPractice.Core.Theory;

/// <summary>A key signature, as a number of fifths: +n sharps, -n flats (-7..7).</summary>
public readonly record struct KeySignature
{
    private static readonly Letter[] SharpOrder = [Letter.F, Letter.C, Letter.G, Letter.D, Letter.A, Letter.E, Letter.B];
    private static readonly Letter[] FlatOrder = [Letter.B, Letter.E, Letter.A, Letter.D, Letter.G, Letter.C, Letter.F];
    private static readonly int[] LetterSemitones = [0, 2, 4, 5, 7, 9, 11];

    private static readonly string[] MajorNames = ["Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#"];
    private static readonly string[] MinorNames = ["Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#", "G#", "D#", "A#"];

    public KeySignature(int fifths)
    {
        if (fifths is < -7 or > 7) throw new ArgumentOutOfRangeException(nameof(fifths));
        Fifths = fifths;
    }

    public int Fifths { get; }

    public static KeySignature CMajor => new(0);

    /// <summary>All 15 key signatures, from 7 flats to 7 sharps.</summary>
    public static IReadOnlyList<KeySignature> All { get; } = Enumerable.Range(-7, 15).Select(f => new KeySignature(f)).ToList();

    public string MajorName => MajorNames[Fifths + 7];
    public string MinorName => MinorNames[Fifths + 7];

    /// <summary>Letters carrying an accidental, in the order they are written.</summary>
    public IReadOnlyList<Letter> AccidentalLetters => Fifths >= 0 ? SharpOrder[..Fifths] : FlatOrder[..(-Fifths)];

    /// <summary>The accidental this key applies to a letter: 1 sharp, -1 flat, 0 none.</summary>
    public int AccidentalFor(Letter letter) => Fifths switch
    {
        > 0 => Array.IndexOf(SharpOrder, letter) < Fifths ? 1 : 0,
        < 0 => Array.IndexOf(FlatOrder, letter) < -Fifths ? -1 : 0,
        _ => 0,
    };

    /// <summary>
    /// Spells a pitch in this key: scale notes use the key's spelling (F# in G major, Cb in Gb major);
    /// other notes use sharps in sharp keys and C major, flats in flat keys.
    /// </summary>
    public SpelledPitch Spell(Pitch pitch)
    {
        foreach (var letter in Enum.GetValues<Letter>())
        {
            var accidental = AccidentalFor(letter);
            if (((LetterSemitones[(int)letter] + accidental) % 12 + 12) % 12 == pitch.PitchClass)
            {
                // Octave belongs to the letter: Cb4 sounds as B3, B#3 as C4.
                var octave = (pitch.MidiNumber - LetterSemitones[(int)letter] - accidental) / 12 - 1;
                return new SpelledPitch(letter, accidental, octave);
            }
        }
        return SpelledPitch.FromPitch(pitch, preferFlats: Fifths < 0);
    }

    /// <summary>
    /// The accidental sign to print in front of a note in this key, or null if none
    /// (0 means a natural sign). Ignores bar context: each chord stands alone.
    /// </summary>
    public int? DisplayedAccidental(SpelledPitch pitch) =>
        pitch.Accidental == AccidentalFor(pitch.Letter) ? null : pitch.Accidental;

    public override string ToString()
    {
        var count = Fifths == 0 ? "" : $" ({Math.Abs(Fifths)}{(Fifths > 0 ? "♯" : "♭")})";
        return $"{MajorName} major / {MinorName} minor{count}";
    }
}
