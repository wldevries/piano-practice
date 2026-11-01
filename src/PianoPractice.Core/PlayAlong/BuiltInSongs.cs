namespace PianoPractice.Core.PlayAlong;

/// <summary>Public-domain songs that ship with the app, written in the <see cref="SongText"/> format.</summary>
public static class BuiltInSongs
{
    private const string Prefix = "Songs/";

    /// <summary>Resource ids, in order of difficulty (file names start with a number).</summary>
    public static IReadOnlyList<string> Ids { get; } = typeof(BuiltInSongs).Assembly
        .GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
        .Select(n => n[Prefix.Length..])
        .Order(StringComparer.Ordinal)
        .ToList();

    public static string Text(string id)
    {
        using var stream = typeof(BuiltInSongs).Assembly.GetManifestResourceStream(Prefix + id)
            ?? throw new ArgumentException($"No built-in song '{id}'.", nameof(id));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Song Load(string id) => SongText.Parse(Text(id));
}
