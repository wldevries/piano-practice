namespace PianoPractice.Core.PlayAlong;

/// <summary>Remembered play-along choices.</summary>
public sealed record PlayAlongOptions
{
    public const int MaxRecentFiles = 10;

    /// <summary>"builtin:&lt;id&gt;" or the path of a MIDI file.</summary>
    public string? LastSong { get; init; }

    public List<string> RecentFiles { get; init; } = [];

    public PracticeHands Hands { get; init; } = PracticeHands.Right;
    public PlayAlongMode Mode { get; init; } = PlayAlongMode.Wait;
    public bool PlayOtherHand { get; init; } = true;
    public int TempoPercent { get; init; } = 75;
    public bool Loop { get; init; }
    public bool ShowNoteNames { get; init; } = true;

    /// <summary>Show sheet music instead of falling notes.</summary>
    public bool SheetMusic { get; init; }

    public PlayAlongOptions WithRecentFile(string path) => this with
    {
        RecentFiles = [path, .. RecentFiles.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).Take(MaxRecentFiles - 1)],
    };
}
