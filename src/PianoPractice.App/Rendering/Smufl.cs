namespace PianoPractice.App.Rendering;

/// <summary>
/// SMuFL code points used with the Bravura font. See https://w3c.github.io/smufl/latest/
/// In SMuFL fonts one em equals four staff spaces, so font size = 4 × staff space.
/// </summary>
public static class Smufl
{
    // Unpackaged app: Win2D can't resolve ms-appx:/// URIs, so use the absolute path next to the exe.
    public static readonly string FontUri =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Bravura.otf") + "#Bravura";

    // Clefs: the glyph origin sits on the line the clef names (G4 line for treble, F3 line for bass).
    public const string GClef = "";
    public const string FClef = "";

    // Noteheads
    public const string NoteheadWhole = "";
    public const string NoteheadHalf = "";
    public const string NoteheadBlack = "";

    // Accidentals
    public const string AccidentalFlat = "";
    public const string AccidentalNatural = "";
    public const string AccidentalSharp = "";
    public const string AccidentalDoubleSharp = "";
    public const string AccidentalDoubleFlat = "";

    // Engraving defaults from Bravura's metadata, in staff spaces.
    public const float StaffLineThickness = 0.13f;
    public const float StemThickness = 0.12f;
    public const float LedgerLineThickness = 0.16f;
    public const float LedgerLineExtension = 0.4f;
    public const float NoteheadBlackWidth = 1.18f;
    public const float StemLength = 3.5f;
}
