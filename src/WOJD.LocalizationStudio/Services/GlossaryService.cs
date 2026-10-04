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

        return GetEntries()
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Source) &&
                source.Contains(x.Source, StringComparison.OrdinalIgnoreCase))
            .Select(x => new GlossaryMatch(
                x.Source,
                x.Target,
                x.Required,
                string.IsNullOrWhiteSpace(x.Target) ||
                translation.Contains(x.Target, StringComparison.OrdinalIgnoreCase),
                x.Notes))
            .ToList();
    }

    public static IReadOnlyList<string> Validate(
        string source,
        string translation)
        => Match(source, translation)
            .Where(x => x.Required && !x.IsSatisfied && !string.IsNullOrWhiteSpace(x.Target))
            .Select(x => $"Глоссарий: «{x.Source}» должно переводиться как «{x.Target}»")
            .ToList();

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
