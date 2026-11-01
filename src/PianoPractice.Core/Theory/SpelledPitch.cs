namespace PianoPractice.Core.Theory;

public enum Letter { C, D, E, F, G, A, B }

/// <summary>
/// A pitch with an explicit spelling: letter + accidental + octave.
/// The octave follows the letter (B#3 sounds as C4), as in standard notation.
/// </summary>
public readonly record struct SpelledPitch(Letter Letter, int Accidental, int Octave)
{
    private static readonly int[] LetterSemitones = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>Accidental in semitones: -2 = double flat, -1 = flat, 0 = natural, 1 = sharp, 2 = double sharp.</summary>
    public int Accidental { get; init; } = Accidental is >= -2 and <= 2
        ? Accidental
        : throw new ArgumentOutOfRangeException(nameof(Accidental));

    public Pitch Pitch => new((Octave + 1) * 12 + LetterSemitones[(int)Letter] + Accidental);

    /// <summary>
    /// Diatonic step index: number of letter steps above C0. Two notes on the same
    /// staff line/space share this value, so it drives staff positioning.
    /// </summary>
    public int DiatonicIndex => Octave * 7 + (int)Letter;

    public static SpelledPitch FromPitch(Pitch pitch, bool preferFlats = false)
    {
        var pc = pitch.PitchClass;
        var octave = pitch.Octave;
        return pc switch
        {
            0 => new(Letter.C, 0, octave),
            1 => preferFlats ? new(Letter.D, -1, octave) : new(Letter.C, 1, octave),
            2 => new(Letter.D, 0, octave),
            3 => preferFlats ? new(Letter.E, -1, octave) : new(Letter.D, 1, octave),
            4 => new(Letter.E, 0, octave),
            5 => new(Letter.F, 0, octave),
            6 => preferFlats ? new(Letter.G, -1, octave) : new(Letter.F, 1, octave),
            7 => new(Letter.G, 0, octave),
            8 => preferFlats ? new(Letter.A, -1, octave) : new(Letter.G, 1, octave),
            9 => new(Letter.A, 0, octave),
            10 => preferFlats ? new(Letter.B, -1, octave) : new(Letter.A, 1, octave),
            _ => new(Letter.B, 0, octave),
        };
    }

    public string AccidentalSymbol => Accidental switch
    {
        -2 => "bb",
        -1 => "b",
        1 => "#",
        2 => "x",
        _ => "",
    };

    public override string ToString() => $"{Letter}{AccidentalSymbol}{Octave}";
}
