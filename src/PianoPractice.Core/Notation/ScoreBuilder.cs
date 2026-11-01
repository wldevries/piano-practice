using PianoPractice.Core.PlayAlong;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Notation;

/// <summary>The notes of one hand struck together, as written: one stem, one rhythm value.</summary>
public sealed class ScoreChord
{
    public required Hand Hand { get; init; }
    public Clef Clef => Hand == Hand.Right ? Clef.Treble : Clef.Bass;

    /// <summary>Song time in seconds (where it's drawn).</summary>
    public required double Start { get; init; }

    /// <summary>0-based bar and position in it, in quarter notes (quantized to sixteenths).</summary>
    public required int Bar { get; init; }
    public required double StartQuarters { get; init; }
    public required double EndQuarters { get; init; }

    public required NoteValue Value { get; init; }

    /// <summary>Indices into <see cref="Song.Notes"/>, in the same order as the spelled pitches given to the layout.</summary>
    public required IReadOnlyList<int> NoteIndices { get; init; }
    public required IReadOnlyList<SpelledPitch> Pitches { get; init; }
    public required ChordLayoutResult Layout { get; set; }

    /// <summary>Accidental sign shown for each pitch (null = none), in <see cref="Pitches"/> order.</summary>
    public required IReadOnlyList<int?> Shown { get; init; }

    /// <summary>Written an octave lower under an 8va line, to avoid a ladder of ledger lines.</summary>
    public bool Ottava { get; set; }

    public bool StemUp { get; set; }

    /// <summary>Index into <see cref="Score.Beams"/>, or -1 for a lone note (with flags).</summary>
    public int Beam { get; set; } = -1;

    public int LowestStep => Layout.Notes.Min(n => n.Step);
    public int HighestStep => Layout.Notes.Max(n => n.Step);
}

/// <param name="WholeBar">A whole-bar rest: drawn centred in the bar, whatever the time signature.</param>
public sealed record ScoreRest(Hand Hand, double Start, NoteValue Value, bool WholeBar, double BarEnd);

/// <summary>Consecutive short notes joined by a beam; <paramref name="Chords"/> index <see cref="Score.Chords"/>.</summary>
public sealed record ScoreBeam(IReadOnlyList<int> Chords, bool StemUp);

/// <summary>A song as notation: right hand on the treble staff, left hand on the bass staff.</summary>
public sealed record Score(KeySignature Key, IReadOnlyList<ScoreChord> Chords, IReadOnlyList<ScoreRest> Rests, IReadOnlyList<ScoreBeam> Beams);

/// <summary>
/// Turns a <see cref="Song"/> (times in seconds) into notation: rhythm values quantized to sixteenths
/// within each bar, spelling from the key, accidentals that carry through the bar, rests, stems and beams.
/// MIDI files don't contain notation, so this is a readable approximation, not engraving.
/// </summary>
public static class ScoreBuilder
{
    private const double ChordTolerance = 0.03; // seconds
    private const double Grid = 0.25;           // quarters: quantize to sixteenths
    private const double Eps = 1e-6;
    private const double LegatoGap = 0.35;      // gaps shorter than this share of the time to the next note are ignored

    public static Score Build(Song song)
    {
        var clock = new BarClock(song);
        var chords = new List<ScoreChord>();
        var rests = new List<ScoreRest>();
        var beams = new List<ScoreBeam>();

        foreach (var hand in new[] { Hand.Right, Hand.Left })
        {
            var handChords = BuildChords(song, clock, hand);
            AddRests(clock, hand, handChords, rests);
            var first = chords.Count;
            chords.AddRange(handChords);
            AddBeams(clock, song, chords, first, beams);
            if (hand == Hand.Right) ApplyOttava(song.Key, chords, first, beams);
        }

        return new Score(song.Key, chords.OrderBy(c => c.Start).ThenBy(c => c.Hand).ToList(), rests, beams)
            .WithBeamIndicesFixed();
    }

    private static List<ScoreChord> BuildChords(Song song, BarClock clock, Hand hand)
    {
        var key = song.Key;
        var clef = hand == Hand.Right ? Clef.Treble : Clef.Bass;

        // Group notes struck together.
        var groups = new List<List<int>>();
        for (var i = 0; i < song.Notes.Count; i++)
        {
            if (song.Notes[i].Hand != hand) continue;
            if (groups.Count > 0 && song.Notes[i].Start - song.Notes[groups[^1][0]].Start <= ChordTolerance) groups[^1].Add(i);
            else groups.Add([i]);
        }

        // Quantized onsets, so each chord knows where the next one starts.
        var onsets = groups.Select(g =>
        {
            var start = song.Notes[g[0]].Start;
            var bar = clock.BarIndex(start);
            var q = Quantize(clock.Quarters(start, bar));
            // A note quantized onto the next bar line belongs to that bar.
            if (q >= clock.Length(bar) - Eps && bar + 1 < clock.Count) return (Bar: bar + 1, Q: 0.0);
            return (Bar: bar, Q: Math.Max(q, 0));
        }).ToList();

        var result = new List<ScoreChord>();
        var accidentals = new Dictionary<(Letter, int), int>();
        var currentBar = -1;
        for (var g = 0; g < groups.Count; g++)
        {
            var (bar, startQ) = onsets[g];
            var barLength = clock.Length(bar);
            var nextQ = g + 1 < groups.Count && onsets[g + 1].Bar == bar ? onsets[g + 1].Q : barLength;
            var duration = groups[g].Min(i => song.Notes[i].Duration) / clock.SecondsPerQuarter(bar);
            // A note released just before the next one (played, not written, short) lasts until it.
            var gap = nextQ - (startQ + duration);
            var endQ = !song.ExactTiming && gap > 0 && gap < Math.Min(LegatoGap * (nextQ - startQ), 0.5)
                ? nextQ
                : Math.Min(Quantize(startQ + duration), Math.Min(nextQ, barLength));
            if (endQ <= startQ + Eps) endQ = startQ + Grid; // squeezed by quantizing: show as a sixteenth

            if (bar != currentBar)
            {
                accidentals.Clear();
                currentBar = bar;
            }

            var pitches = groups[g].Select(i => key.Spell(song.Notes[i].Pitch)).ToList();
            var shown = new Dictionary<SpelledPitch, int?>();
            foreach (var p in pitches)
            {
                var inEffect = accidentals.TryGetValue((p.Letter, p.Octave), out var a) ? a : key.AccidentalFor(p.Letter);
                shown[p] = inEffect == p.Accidental ? null : p.Accidental;
            }
            foreach (var p in pitches) accidentals[(p.Letter, p.Octave)] = p.Accidental;

            var layout = ChordLayout.Compute(pitches, clef, key, p => shown.GetValueOrDefault(p));
            result.Add(new ScoreChord
            {
                Hand = hand,
                Start = song.Notes[groups[g][0]].Start,
                Bar = bar,
                StartQuarters = startQ,
                EndQuarters = endQ,
                Value = NoteValue.Nearest(endQ - startQ),
                NoteIndices = groups[g],
                Pitches = pitches,
                Shown = pitches.Select(p => shown[p]).ToList(),
                Layout = layout,
                StemUp = layout.Notes.Average(n => n.Step) < StaffPosition.MiddleLine,
            });
        }
        return result;
    }

    private static void AddRests(BarClock clock, Hand hand, List<ScoreChord> chords, List<ScoreRest> rests)
    {
        var byBar = chords.GroupBy(c => c.Bar).ToDictionary(g => g.Key, g => g.ToList());
        for (var bar = 0; bar < clock.Count; bar++)
        {
            var length = clock.Length(bar);
            if (!byBar.TryGetValue(bar, out var inBar))
            {
                rests.Add(new ScoreRest(hand, clock.Time(bar, 0), new NoteValue(NoteValueKind.Whole), true, clock.Time(bar, length)));
                continue;
            }

            var cursor = 0.0;
            foreach (var chord in inBar)
            {
                Fill(cursor, chord.StartQuarters);
                cursor = Math.Max(cursor, chord.EndQuarters);
            }
            Fill(cursor, length);

            void Fill(double from, double to)
            {
                while (to - from >= Grid - Eps)
                {
                    // Rests start on their own beat: after a quarter in 4/4 it's a quarter rest, then a half.
                    var value = NoteValue.LargestFitting(to - from, alignedAt: from);
                    rests.Add(new ScoreRest(hand, clock.Time(bar, from), value, false, clock.Time(bar, length)));
                    from += value.Quarters;
                }
            }
        }
    }

    /// <summary>Beams join flagged notes that follow each other without a gap within one beat.</summary>
    private static void AddBeams(BarClock clock, Song song, List<ScoreChord> chords, int first, List<ScoreBeam> beams)
    {
        var run = new List<int>();

        void Close()
        {
            if (run.Count >= 2)
            {
                var up = run.SelectMany(i => chords[i].Layout.Notes).Average(n => n.Step) < StaffPosition.MiddleLine;
                foreach (var i in run)
                {
                    chords[i].StemUp = up;
                    chords[i].Beam = beams.Count;
                }
                beams.Add(new ScoreBeam(run.ToList(), up));
            }
            run.Clear();
        }

        for (var i = first; i < chords.Count; i++)
        {
            var chord = chords[i];
            if (chord.Value.Flags == 0)
            {
                Close();
                continue;
            }
            if (run.Count > 0)
            {
                var previous = chords[run[^1]];
                var group = clock.BeamGroup;
                var sameBeat = previous.Bar == chord.Bar
                    && Math.Abs(previous.EndQuarters - chord.StartQuarters) < Eps
                    && Math.Floor(chord.StartQuarters / group + Eps) == Math.Floor(chords[run[0]].StartQuarters / group + Eps);
                if (!sameBeat) Close();
            }
            run.Add(i);
        }
        Close();
    }

    /// <summary>Treble step from which notes are written an octave lower (E6: three ledger lines above the staff).</summary>
    public const int OttavaFromStep = 14;

    /// <summary>High passages (a beamed group, or a lone chord, centred at E6 or above) go under an 8va line.</summary>
    private static void ApplyOttava(KeySignature key, List<ScoreChord> chords, int first, List<ScoreBeam> beams)
    {
        var units = chords.Skip(first).Select((c, i) => (Chord: c, Index: first + i))
            .GroupBy(x => x.Chord.Beam >= 0 ? x.Chord.Beam : -1 - x.Index); // beam groups together, lone chords alone
        foreach (var unit in units)
        {
            var members = unit.Select(x => x.Chord).ToList();
            if (members.SelectMany(c => c.Layout.Notes).Average(n => n.Step) < OttavaFromStep) continue;

            foreach (var chord in members)
            {
                var lower = chord.Pitches.Select(p => p with { Octave = p.Octave - 1 }).ToList();
                var shown = new Dictionary<SpelledPitch, int?>();
                for (var i = 0; i < lower.Count; i++) shown[lower[i]] = chord.Shown[i];
                chord.Layout = ChordLayout.Compute(lower, chord.Clef, key, p => shown.GetValueOrDefault(p));
                chord.Ottava = true;
                chord.StemUp = chord.Layout.Notes.Average(n => n.Step) < StaffPosition.MiddleLine;
            }

            if (members[0].Beam >= 0)
            {
                var up = members.SelectMany(c => c.Layout.Notes).Average(n => n.Step) < StaffPosition.MiddleLine;
                foreach (var chord in members) chord.StemUp = up;
                beams[members[0].Beam] = beams[members[0].Beam] with { StemUp = up };
            }
        }
    }

    /// <summary>Chords are re-sorted by time at the end, so beams must refer to the final positions.</summary>
    private static Score WithBeamIndicesFixed(this Score score)
    {
        var position = score.Chords.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        var byBeam = score.Chords.Where(c => c.Beam >= 0).GroupBy(c => c.Beam).OrderBy(g => g.Key)
            .Select(g => new ScoreBeam(g.Select(c => position[c]).Order().ToList(), g.First().StemUp))
            .ToList();
        for (var b = 0; b < byBeam.Count; b++)
        {
            foreach (var i in byBeam[b].Chords) score.Chords[i].Beam = b;
        }
        return score with { Beams = byBeam };
    }

    private static double Quantize(double quarters) => Math.Round(quarters / Grid) * Grid;

    /// <summary>Converts between song seconds and (bar, quarters into the bar), bar by bar, so tempo changes are handled.</summary>
    private sealed class BarClock
    {
        private readonly IReadOnlyList<double> _starts;
        private readonly double[] _secondsPerQuarter;
        private readonly double[] _length;

        public BarClock(Song song)
        {
            _starts = song.BarStarts;
            Count = _starts.Count;
            var barQuarters = song.BeatsPerBar * 4.0 / song.BeatUnit;
            var nominal = 60.0 / song.BeatsPerMinute;
            BeamGroup = song.BeatUnit == 8 && song.BeatsPerBar % 3 == 0 ? 1.5 : 1.0;

            _secondsPerQuarter = new double[Count];
            _length = new double[Count];
            for (var i = 0; i < Count; i++)
            {
                _secondsPerQuarter[i] = i + 1 < Count ? (_starts[i + 1] - _starts[i]) / barQuarters : double.NaN;
                _length[i] = barQuarters;
            }
            if (Count >= 2) _secondsPerQuarter[Count - 1] = _secondsPerQuarter[Count - 2];
            if (Count >= 3 && Math.Abs(_secondsPerQuarter[0] - _secondsPerQuarter[1]) > 0.05 * _secondsPerQuarter[1])
            {
                // A short first bar is a pickup: same tempo as bar 2, fewer beats.
                _secondsPerQuarter[0] = _secondsPerQuarter[1];
                _length[0] = Quantize((_starts[1] - _starts[0]) / _secondsPerQuarter[0]);
            }
            for (var i = 0; i < Count; i++)
            {
                if (!(_secondsPerQuarter[i] > 0)) _secondsPerQuarter[i] = nominal;
            }
        }

        public int Count { get; }

        /// <summary>Length of a beam group in quarters: a beat, or a dotted quarter in 3/8, 6/8, ...</summary>
        public double BeamGroup { get; }

        public int BarIndex(double time)
        {
            var bar = 0;
            while (bar + 1 < Count && _starts[bar + 1] <= time + 1e-6) bar++;
            return bar;
        }

        public double SecondsPerQuarter(int bar) => _secondsPerQuarter[bar];
        public double Length(int bar) => _length[bar];
        public double Quarters(double time, int bar) => (time - _starts[bar]) / _secondsPerQuarter[bar];
        public double Time(int bar, double quarters) => _starts[bar] + quarters * _secondsPerQuarter[bar];
    }
}
