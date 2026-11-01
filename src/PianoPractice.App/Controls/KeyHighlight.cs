namespace PianoPractice.App.Controls;

public enum KeyHighlightKind
{
    /// <summary>Pressed on the piano.</summary>
    Played,
    /// <summary>Clicked on screen / played by the app.</summary>
    Sounded,
    Correct,
    Wrong,
    Hint,
    /// <summary>Keys in between, e.g. the half steps of an interval being explained.</summary>
    Range,
    /// <summary>Play-along: a right-hand note.</summary>
    RightHand,
    /// <summary>Play-along: a left-hand note.</summary>
    LeftHand,
}

/// <summary>A key to colour on the <see cref="PianoKeyboard"/>. Intensity 0..1 (e.g. velocity).</summary>
public readonly record struct KeyHighlight(int MidiNumber, KeyHighlightKind Kind, float Intensity = 1f);
