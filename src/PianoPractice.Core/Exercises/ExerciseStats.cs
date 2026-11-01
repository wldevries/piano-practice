using System.Text.Json;

namespace PianoPractice.Core.Exercises;

/// <summary>All-time results for one practice item (a note card, an interval, ...).</summary>
public sealed class CardStats
{
    public int Attempts { get; set; }
    public int Correct { get; set; }

    /// <summary>Exponential moving average of the answer time, so recent progress counts most.</summary>
    public double AverageMs { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Accuracy with a gentle prior (one right, one wrong) so a single answer doesn't swing it to 0 or 100%.</summary>
    public double SmoothedAccuracy => (Correct + 1.0) / (Attempts + 2.0);

    public double Accuracy => Attempts == 0 ? 0 : (double)Correct / Attempts;

    public void Record(bool correct, double ms, DateTimeOffset now)
    {
        AverageMs = Attempts == 0 ? ms : AverageMs * 0.7 + ms * 0.3;
        Attempts++;
        if (correct) Correct++;
        LastSeen = now;
    }
}

public sealed record SessionSummary(
    DateTimeOffset Started,
    string Options,
    int Cards,
    int Correct,
    double AverageMs)
{
    public double Accuracy => Cards == 0 ? 0 : (double)Correct / Cards;
}

/// <summary>A wrong first answer, kept so patterns can be found later.</summary>
/// <param name="ItemId">What was asked, e.g. "Ascending:7".</param>
/// <param name="Kind">Exercise-specific category, e.g. "NearMiss".</param>
/// <param name="Expected">e.g. "C4 → G4".</param>
/// <param name="Given">e.g. "F#4 (TT)".</param>
public sealed record Mistake(DateTimeOffset Time, string ItemId, string Kind, string Expected, string Given);

/// <summary>Per-item statistics and session history of one exercise.</summary>
public sealed class ExerciseStats
{
    public const int MaxMistakes = 5000;

    /// <summary>Stats per item id. (Named "Cards" for compatibility with the first stats files.)</summary>
    public Dictionary<string, CardStats> Cards { get; set; } = new();

    public List<SessionSummary> Sessions { get; set; } = new();

    public List<Mistake> Mistakes { get; set; } = new();

    public void AddMistake(Mistake mistake)
    {
        Mistakes.Add(mistake);
        if (Mistakes.Count > MaxMistakes) Mistakes.RemoveRange(0, Mistakes.Count - MaxMistakes);
    }

    public CardStats? For(string id) => Cards.GetValueOrDefault(id);

    public CardStats GetOrAdd(string id)
    {
        if (!Cards.TryGetValue(id, out var stats))
        {
            stats = new CardStats();
            Cards[id] = stats;
        }
        return stats;
    }
}

/// <summary>Persists <see cref="ExerciseStats"/> as JSON, one file per exercise.</summary>
public sealed class ExerciseStatsStore
{
    private const int MaxSessions = 500;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string? _path;

    /// <param name="folder">Where to keep the file; null keeps everything in memory (tests).</param>
    /// <param name="fileName">e.g. "note-reading-stats.json".</param>
    public ExerciseStatsStore(string? folder, string fileName)
    {
        _path = folder is null ? null : Path.Combine(folder, fileName);
        Data = Load();
    }

    public static ExerciseStatsStore InMemory() => new(null, "");

    public ExerciseStats Data { get; }

    public void AddSession(SessionSummary summary)
    {
        Data.Sessions.Add(summary);
        if (Data.Sessions.Count > MaxSessions) Data.Sessions.RemoveRange(0, Data.Sessions.Count - MaxSessions);
        Save();
    }

    public void Save()
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Data, JsonOptions));
    }

    private ExerciseStats Load()
    {
        if (_path is null || !File.Exists(_path)) return new ExerciseStats();
        try
        {
            return JsonSerializer.Deserialize<ExerciseStats>(File.ReadAllText(_path)) ?? new ExerciseStats();
        }
        catch (JsonException)
        {
            // Keep the broken file for inspection rather than silently overwriting it later.
            File.Move(_path, _path + ".broken", overwrite: true);
            return new ExerciseStats();
        }
    }
}
