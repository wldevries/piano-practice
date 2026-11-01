using System.Text.Json;

namespace PianoPractice.Core.Settings;

public sealed record AppSettings
{
    public string? MidiInput { get; init; }
    public string? MidiOutput { get; init; }

    /// <summary>Key signature on the Live page, in fifths (-7..7).</summary>
    public int LiveKeyFifths { get; init; }
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in the given folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore(string folder)
    {
        _path = Path.Combine(folder, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
    }

    private AppSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings(); // corrupt file: start fresh, it's rewritten on next change
        }
    }
}
