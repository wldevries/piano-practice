using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Exercises.Intervals;

/// <summary>What kind of wrong first answer it was, so we can tell an ear problem from a keyboard problem.</summary>
public enum IntervalMistakeKind
{
    /// <summary>Picked or played another interval from the practised set: the ear mixed them up.</summary>
    OtherInterval,
    /// <summary>Right note name, wrong octave.</summary>
    WrongOctave,
    /// <summary>Right size, but went down instead of up (or the other way).</summary>
    WrongDirection,
    /// <summary>One or two half steps away from the right key: probably heard it, missed the key.</summary>
    NearMiss,
    /// <summary>Anything else.</summary>
    Other,
}

public static class IntervalMistakes
{
    /// <summary>Classifies a wrong key in play-back mode.</summary>
    /// <param name="practised">The interval sizes in this session's set.</param>
    public static (IntervalMistakeKind Kind, int PlayedSemitones) Classify(IntervalQuestion question, Pitch played, IReadOnlyCollection<int> practised)
    {
        var root = question.Root.MidiNumber;
        var target = question.Target.MidiNumber;
        var semis = question.Item.Semitones;
        var down = question.Item.Direction == IntervalDirection.Descending;

        // Distance from the root in the asked direction (negative = the other way).
        var playedSemis = down ? root - played.MidiNumber : played.MidiNumber - root;

        if (played.PitchClass == question.Target.PitchClass) return (IntervalMistakeKind.WrongOctave, playedSemis);
        if (playedSemis == -semis) return (IntervalMistakeKind.WrongDirection, playedSemis);
        if (playedSemis > 0 && practised.Contains(playedSemis)) return (IntervalMistakeKind.OtherInterval, playedSemis);
        if (Math.Abs(played.MidiNumber - target) <= 2) return (IntervalMistakeKind.NearMiss, playedSemis);
        return (IntervalMistakeKind.Other, playedSemis);
    }

    public static string Describe(IntervalMistakeKind kind) => kind switch
    {
        IntervalMistakeKind.OtherInterval => "heard another interval",
        IntervalMistakeKind.WrongOctave => "right note, wrong octave",
        IntervalMistakeKind.WrongDirection => "wrong direction",
        IntervalMistakeKind.NearMiss => "close (1–2 half steps off)",
        _ => "further off",
    };

    /// <summary>"P5" for 1..12, otherwise the number of half steps (negative = wrong way).</summary>
    public static string SizeName(int semitones) => semitones switch
    {
        >= 1 and <= 12 => IntervalCatalog.Get(semitones).Short,
        0 => "same note",
        < 0 => $"{-semitones} half steps the wrong way",
        _ => NoteNames.Interval(semitones),
    };
}
