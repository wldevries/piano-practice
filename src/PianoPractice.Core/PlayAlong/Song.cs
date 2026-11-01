using PianoPractice.Core.Theory;

namespace PianoPractice.Core.PlayAlong;

public enum Hand { Right, Left }

/// <summary>One note of a song. Times are in seconds at 100% tempo.</summary>
public sealed record SongNote(Pitch Pitch, double Start, double Duration, Hand Hand, int Velocity = 80)
{
    public double End => Start + Duration;
}

/// <summary>A piece to play along with: notes split into hands, plus bar positions for looping.</summary>
public sealed class Song
{
    public Song(string title, IEnumerable<SongNote> notes, IReadOnlyList<double> barStarts, double beatsPerMinute = 120,
        int beatsPerBar = 4, int beatUnit = 4, KeySignature key = default, string? composer = null, bool exactTiming = false)
    {
        Title = title;
        Composer = composer;
        ExactTiming = exactTiming;
        Notes = notes.OrderBy(n => n.Start).ThenBy(n => n.Pitch).ToList();
        BarStarts = barStarts.Count > 0 ? barStarts : [0];
        BeatsPerMinute = beatsPerMinute;
        BeatsPerBar = beatsPerBar;
        BeatUnit = beatUnit;
        Key = key;
        Duration = Notes.Count == 0 ? 0 : Notes.Max(n => n.End);
    }

    public string Title { get; }
    public string? Composer { get; }

    /// <summary>Written, not played: note lengths are exact, so a gap before the next note is a real rest.</summary>
    public bool ExactTiming { get; }

    /// <summary>Sorted by start time, then pitch.</summary>
    public IReadOnlyList<SongNote> Notes { get; }

    /// <summary>Start time of each bar in seconds; bar 1 is index 0 (a pickup bar counts as bar 1).</summary>
    public IReadOnlyList<double> BarStarts { get; }

    public int BarCount => BarStarts.Count;

    /// <summary>Quarter notes per minute (the first tempo for MIDI files).</summary>
    public double BeatsPerMinute { get; }

    public int BeatsPerBar { get; }
    public int BeatUnit { get; }
    public KeySignature Key { get; }

    /// <summary>End of the last note, in seconds.</summary>
    public double Duration { get; }

    /// <summary>Start of a 1-based bar; one past the last bar gives the end of the song.</summary>
    public double BarStart(int bar) =>
        bar <= 1 ? 0 : bar > BarCount ? Math.Max(Duration, BarStarts[^1]) : BarStarts[bar - 1];

    /// <summary>1-based bar containing a time.</summary>
    public int BarAt(double time)
    {
        var bar = 1;
        for (var i = 1; i < BarStarts.Count && BarStarts[i] <= time + 1e-6; i++) bar = i + 1;
        return bar;
    }

    public bool Has(Hand hand) => Notes.Any(n => n.Hand == hand);

    /// <summary>Lowest and highest pitch, or middle C if the song is empty.</summary>
    public (Pitch Low, Pitch High) Range => Notes.Count == 0
        ? (Pitch.MiddleC, Pitch.MiddleC)
        : (Notes.Min(n => n.Pitch), Notes.Max(n => n.Pitch));
}
