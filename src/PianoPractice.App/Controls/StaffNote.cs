using PianoPractice.Core.Theory;

namespace PianoPractice.App.Controls;

/// <summary>A note to show on the <see cref="GrandStaff"/>. Kind null = normal ink colour.</summary>
public readonly record struct StaffNote(SpelledPitch Pitch, KeyHighlightKind? Kind = null);
