using System.Globalization;
using System.Text;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.PlayAlong;

/// <summary>
/// A small text format for writing songs by hand:
/// <code>
/// title: Ode to Joy
/// tempo: 100        # quarter notes per minute
/// time: 4/4
/// key: 0            # fifths: -1 = F major, 1 = G major
/// pickup: 1         # beats before the first full bar (optional)
/// RH: E4/q E4 F4 G4 | G4 F4 E4 D4 |
/// LH: C3/w | [G2 B2]/w |
/// </code>
/// Durations: w h q 8 16, a dot for dotted, a trailing t for triplets (8t). A duration carries
/// over to the following notes until a new one is given. <c>r</c> is a rest, <c>[..]</c> a chord.
/// Every <c>|</c> checks that the bar is full, which catches transcription mistakes.
/// Several RH:/LH: lines are joined in order.
/// </summary>
public static class SongText
{
    public static Song Parse(string text)
    {
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hands = new Dictionary<Hand, StringBuilder> { [Hand.Right] = new(), [Hand.Left] = new() };

        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0) throw new FormatException($"Line {lineNumber}: expected 'name: value'.");
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (name.Equals("RH", StringComparison.OrdinalIgnoreCase)) hands[Hand.Right].Append(' ').Append(value);
            else if (name.Equals("LH", StringComparison.OrdinalIgnoreCase)) hands[Hand.Left].Append(' ').Append(value);
            else header[name] = value;
        }

        var title = header.GetValueOrDefault("title") ?? "Untitled";
        var bpm = double.Parse(header.GetValueOrDefault("tempo") ?? "100", CultureInfo.InvariantCulture);
        var (beatsPerBar, beatUnit) = ParseTime(header.GetValueOrDefault("time") ?? "4/4");
        var key = new KeySignature(int.Parse(header.GetValueOrDefault("key") ?? "0", CultureInfo.InvariantCulture));
        var pickup = double.Parse(header.GetValueOrDefault("pickup") ?? "0", CultureInfo.InvariantCulture);

        // Bar length in quarter-note beats: 3/8 is 1.5 quarters.
        var barBeats = beatsPerBar * 4.0 / beatUnit;
        var secondsPerBeat = 60.0 / bpm;

        var notes = new List<SongNote>();
        var lengthInBeats = 0.0;
        foreach (var (hand, source) in hands)
        {
            var end = ParseHand(source.ToString(), hand, barBeats, pickup, secondsPerBeat, notes);
            lengthInBeats = Math.Max(lengthInBeats, end);
        }

        var barStarts = new List<double>();
        for (var beat = pickup > 0 ? -barBeats + pickup : 0; beat < lengthInBeats - 1e-6; beat += barBeats)
        {
            barStarts.Add(Math.Max(beat, 0) * secondsPerBeat);
        }

        return new Song(title, notes, barStarts, bpm, beatsPerBar, beatUnit, key, header.GetValueOrDefault("composer"), exactTiming: true);
    }

    /// <summary>Parses one hand; returns where it ends, in beats.</summary>
    private static double ParseHand(string source, Hand hand, double barBeats, double pickup, double secondsPerBeat, List<SongNote> notes)
    {
        var beat = 0.0;
        var barStartBeat = 0.0;
        var barNumber = 1;
        var duration = 1.0;

        foreach (var token in Tokenize(source))
        {
            if (token == "|")
            {
                var expected = barNumber == 1 && pickup > 0 ? pickup : barBeats;
                var actual = beat - barStartBeat;
                if (Math.Abs(actual - expected) > 1e-6)
                {
                    throw new FormatException($"{hand} hand, bar {barNumber}: has {actual:0.###} beats, expected {expected:0.###}.");
                }
                barStartBeat = beat;
                barNumber++;
                continue;
            }

            var (pitches, length) = ParseEvent(token, hand, barNumber);
            if (length is { } l) duration = l;

            foreach (var pitch in pitches)
            {
                notes.Add(new SongNote(pitch, beat * secondsPerBeat, duration * secondsPerBeat, hand));
            }
            beat += duration;
        }
        return beat;
    }

    private static (List<Pitch> Pitches, double? Duration) ParseEvent(string token, Hand hand, int bar)
    {
        string body;
        string? durationText = null;
        var slash = token.LastIndexOf('/');
        if (slash >= 0 && (token[0] != '[' || slash > token.IndexOf(']')))
        {
            body = token[..slash];
            durationText = token[(slash + 1)..];
        }
        else
        {
            body = token;
        }

        var pitches = new List<Pitch>();
        if (body.StartsWith('['))
        {
            if (!body.EndsWith(']')) throw new FormatException($"{hand} hand, bar {bar}: unclosed chord '{token}'.");
            foreach (var part in body[1..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                pitches.Add(ParsePitch(part, hand, bar));
            }
        }
        else if (!body.Equals("r", StringComparison.OrdinalIgnoreCase))
        {
            pitches.Add(ParsePitch(body, hand, bar));
        }

        return (pitches, durationText is null ? null : ParseDuration(durationText, hand, bar));
    }

    private static Pitch ParsePitch(string text, Hand hand, int bar) =>
        SpelledPitch.TryParse(text, out var spelled)
            ? spelled.Pitch
            : throw new FormatException($"{hand} hand, bar {bar}: '{text}' is not a note (expected e.g. C4, F#3, Bb2).");

    private static double ParseDuration(string text, Hand hand, int bar)
    {
        var dotted = text.EndsWith('.');
        if (dotted) text = text[..^1];
        var triplet = text.EndsWith('t');
        if (triplet) text = text[..^1];

        double beats = text switch
        {
            "w" => 4,
            "h" => 2,
            "q" => 1,
            "8" => 0.5,
            "16" => 0.25,
            "32" => 0.125,
            _ => throw new FormatException($"{hand} hand, bar {bar}: unknown duration '/{text}'."),
        };
        if (dotted) beats *= 1.5;
        if (triplet) beats *= 2.0 / 3.0;
        return beats;
    }

    /// <summary>Splits on spaces, keeping "[C4 E4]/h" and "|" as single tokens.</summary>
    private static IEnumerable<string> Tokenize(string source)
    {
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '|') { i++; yield return "|"; continue; }

            var start = i;
            if (c == '[')
            {
                var close = source.IndexOf(']', i);
                i = close < 0 ? source.Length : close + 1;
            }
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] != '|') i++;
            yield return source[start..i];
        }
    }

    private static (int, int) ParseTime(string text)
    {
        var parts = text.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var n) || !int.TryParse(parts[1], out var d) || n <= 0 || d <= 0)
        {
            throw new FormatException($"Time signature '{text}' should look like 3/4.");
        }
        return (n, d);
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        // '#' is also a sharp (F#4): only treat it as a comment at the start or after whitespace.
        while (hash > 0 && !char.IsWhiteSpace(line[hash - 1])) hash = line.IndexOf('#', hash + 1);
        return hash >= 0 ? line[..hash] : line;
    }
}
