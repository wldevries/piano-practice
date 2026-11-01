namespace PianoPractice.Core.PlayAlong;

/// <summary>
/// Splits one part into right and left hand by following where each hand is, rather than at a fixed
/// pitch. Notes struck together are split where it costs least (each note's distance to its hand's recent
/// position, plus a penalty for a hand stretching past an octave); a lone note goes to the nearer hand.
/// A low voice that wanders above middle C (or a melody that dips below it) then stays in one hand.
/// </summary>
public static class HandSplitter
{
    private const double ChordTolerance = 0.03; // seconds
    private const int Reach = 12;               // a comfortable stretch for one hand, in semitones
    private const double Follow = 0.35;         // how quickly a hand's position follows its notes
    private const double MinSeparation = 5;     // semitones between hand positions

    public static List<SongNote> Split(IEnumerable<SongNote> notes, double rightStart = 67, double leftStart = 48)
    {
        var sorted = notes.OrderBy(n => n.Start).ThenBy(n => n.Pitch).ToList();
        var result = new List<SongNote>(sorted.Count);
        double right = rightStart, left = leftStart;

        var i = 0;
        while (i < sorted.Count)
        {
            var j = i + 1;
            while (j < sorted.Count && sorted[j].Start - sorted[i].Start <= ChordTolerance) j++;
            var group = sorted.GetRange(i, j - i); // sorted by pitch
            i = j;

            // Notes below index k go to the left hand.
            var best = 0;
            var bestCost = double.MaxValue;
            for (var k = 0; k <= group.Count; k++)
            {
                var cost = Cost(group, 0, k, left) + Cost(group, k, group.Count, right);
                if (cost < bestCost - 1e-9 || (Math.Abs(cost - bestCost) < 1e-9 && PreferSplit(group, k, best)))
                {
                    bestCost = cost;
                    best = k;
                }
            }

            for (var n = 0; n < group.Count; n++) result.Add(group[n] with { Hand = n < best ? Hand.Left : Hand.Right });

            if (best > 0) left += Follow * (Mean(group, 0, best) - left);
            if (best < group.Count) right += Follow * (Mean(group, best, group.Count) - right);
            if (right - left < MinSeparation)
            {
                var mid = (right + left) / 2;
                right = mid + MinSeparation / 2;
                left = mid - MinSeparation / 2;
            }
        }
        return result;
    }

    private static double Cost(List<SongNote> group, int from, int to, double centre)
    {
        if (from >= to) return 0;
        var cost = 0.0;
        for (var n = from; n < to; n++) cost += Math.Abs(group[n].Pitch.MidiNumber - centre);
        var span = group[to - 1].Pitch.MidiNumber - group[from].Pitch.MidiNumber;
        if (span > Reach) cost += (span - Reach) * 10;
        return cost;
    }

    /// <summary>On a tie, middle C and above goes to the right hand.</summary>
    private static bool PreferSplit(List<SongNote> group, int k, int current)
    {
        int Score(int split) => Enumerable.Range(0, group.Count)
            .Count(n => (n < split) == (group[n].Pitch.MidiNumber < MidiSongFile.SplitPoint));
        return Score(k) > Score(current);
    }

    private static double Mean(List<SongNote> group, int from, int to)
    {
        var sum = 0.0;
        for (var n = from; n < to; n++) sum += group[n].Pitch.MidiNumber;
        return sum / (to - from);
    }
}
