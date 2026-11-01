using PianoPractice.Core.Notation;
using PianoPractice.Core.Theory;

namespace PianoPractice.App.Controls;

/// <summary>
/// A note to show on the <see cref="GrandStaff"/>. Kind null = normal ink colour.
/// Clef null = decided by the staff's split point; set it to force a staff (e.g. D4 on the bass staff).
/// </summary>
public readonly record struct StaffNote(SpelledPitch Pitch, KeyHighlightKind? Kind = null, Clef? Clef = null);
