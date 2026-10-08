using System.IO;
using System.Text;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class GlossaryEntry
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}

public sealed record GlossaryMatch(
    string Source,
    string Target,
    bool Required,
    bool IsSatisfied,
    string Notes);

public static class GlossaryService
{
    private static readonly object Sync = new();
    private static IReadOnlyList<GlossaryEntry>? _entries;

    public static string SettingsPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "glossary.json");

    public static IReadOnlyList<GlossaryEntry> GetEntries()
    {
        lock (Sync)
            return _entries ??= LoadCore();
    }

    public static IReadOnlyList<GlossaryMatch> Match(
        string source,
        string translation)
    {
        source ??= string.Empty;
        translation ??= string.Empty;

        var entries = GetEntries();
        if (entries.Count == 0 || source.Length == 0)
            return [];

        List<GlossaryMatch>? matches = null;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Source) ||
                !source.Contains(entry.Source, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matches ??= [];
            matches.Add(new GlossaryMatch(
                entry.Source,
                entry.Target,
                entry.Required,
                string.IsNullOrWhiteSpace(entry.Target) ||
                translation.Contains(entry.Target, StringComparison.OrdinalIgnoreCase),
                entry.Notes));
        }

        return matches ?? [];
    }

    public static IReadOnlyList<string> Validate(
        string source,
        string translation)
    {
        var entries = GetEntries();
        if (entries.Count == 0 || string.IsNullOrEmpty(source))
            return [];

        List<string>? issues = null;
        foreach (var entry in entries)
        {
            if (!entry.Required ||
                string.IsNullOrWhiteSpace(entry.Source) ||
                string.IsNullOrWhiteSpace(entry.Target) ||
                !source.Contains(entry.Source, StringComparison.OrdinalIgnoreCase) ||
                translation.Contains(entry.Target, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            issues ??= [];
            issues.Add($"Глоссарий: «{entry.Source}» должно переводиться как «{entry.Target}»");
        }

        return issues ?? [];
    }

    public static void Save(IEnumerable<GlossaryEntry> entries)
    {
        var normalized = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Source))
            .Select(x => new GlossaryEntry
            {
                Source = x.Source.Trim(),
                Target = x.Target.Trim(),
                Required = x.Required,
                Notes = x.Notes.Trim()
            })
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(normalized, JsonOptions),
                new UTF8Encoding(false));
            File.Move(temp, SettingsPath, true);
            _entries = normalized;
        }
    }

    public static void Reload()
    {
        lock (Sync)
            _entries = null;
    }

    private static IReadOnlyList<GlossaryEntry> LoadCore()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Save([]);
                return _entries ?? [];
            }

            return JsonSerializer.Deserialize<List<GlossaryEntry>>(
                       File.ReadAllText(SettingsPath, Encoding.UTF8),
                       JsonOptions)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
