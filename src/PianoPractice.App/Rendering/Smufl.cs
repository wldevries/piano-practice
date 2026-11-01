namespace PianoPractice.App.Rendering;

/// <summary>
/// SMuFL code points used with the Bravura font. See https://w3c.github.io/smufl/latest/
/// In SMuFL fonts one em equals four staff spaces, so font size = 4 × staff space.
/// Glyph origins (baseline) sit on the line/space the glyph belongs to.
/// </summary>
public static class Smufl
{
    // Unpackaged app: Win2D can't resolve ms-appx:/// URIs, so use the absolute path next to the exe.
    public static readonly string FontUri =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Bravura.otf") + "#Bravura";

    public const string Brace = "";

    // Clefs: the origin sits on the line the clef names (G4 line for treble, F3 line for bass).
    public const string GClef = "";
    public const string FClef = "";

    // Noteheads
    public const string NoteheadWhole = "";
    public const string NoteheadHalf = "";
    public const string NoteheadBlack = "";

    // Flags (origin at the end of the stem), rests (origin on the middle line; the whole rest hangs from the 4th line), dots.
    public const string Flag8thUp = "";
    public const string Flag8thDown = "";
    public const string Flag16thUp = "";
    public const string Flag16thDown = "";
    public const string Flag32ndUp = "";
    public const string Flag32ndDown = "";
    public const string RestWhole = "";
    public const string RestHalf = "";
    public const string RestQuarter = "";
    public const string Rest8th = "";
    public const string Rest16th = "";
    public const string Rest32nd = "";
    public const string AugmentationDot = "";

    // Accidentals
    public const string AccidentalFlat = "";
    public const string AccidentalNatural = "";
    public const string AccidentalSharp = "";
    public const string AccidentalDoubleSharp = "";
    public const string AccidentalDoubleFlat = "";

    public static string Accidental(int accidental) => accidental switch
    {
        -2 => AccidentalDoubleFlat,
        -1 => AccidentalFlat,
        1 => AccidentalSharp,
        2 => AccidentalDoubleSharp,
        _ => AccidentalNatural,
    };

    // Engraving defaults and glyph sizes from Bravura's metadata, in staff spaces.
    public const float StaffLineThickness = 0.13f;
    public const float ThinBarlineThickness = 0.16f;
    public const float LedgerLineThickness = 0.16f;
    public const float LedgerLineExtension = 0.4f;
    public const float StemThickness = 0.12f;
    public const float NoteheadBlackWidth = 1.18f;
    public const float NoteheadWholeWidth = 1.69f;
    public const float BraceWidth = 0.33f; // at its nominal height of one staff (4 spaces)
    public const float BeamThickness = 0.5f;
    public const float BeamSpacing = 0.25f;
    public const float NoteheadHalfWidth = 1.18f;
}
