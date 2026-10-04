using System.IO;
using System.Text;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class GlossaryEntry
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
    public bool CaseSensitive { get; set; }
    public string Note { get; set; } = string.Empty;
}

public static class GlossaryService
{
    private static readonly object Sync = new();
    private static IReadOnlyList<GlossaryEntry>? _entries;
    private static long _version = 1;

    public static long Version => Interlocked.Read(ref _version);

    public static string SettingsPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "glossary.json");

    public static IReadOnlyList<GlossaryEntry> GetEntries()
    {
        lock (Sync)
        {
            if (_entries is not null)
                return _entries;

            _entries = LoadCore();
            return _entries;
        }
    }

    public static void Reload()
    {
        lock (Sync)
            _entries = null;

        Interlocked.Increment(ref _version);
    }

    public static void Save(IEnumerable<GlossaryEntry> entries)
    {
        var normalized = entries
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Source) &&
                !string.IsNullOrWhiteSpace(x.Target))
            .Select(x => new GlossaryEntry
            {
                Source = x.Source.Trim(),
                Target = x.Target.Trim(),
                Required = x.Required,
                CaseSensitive = x.CaseSensitive,
                Note = x.Note?.Trim() ?? string.Empty
            })
            .GroupBy(
                x => x.Source,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Source, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(normalized, JsonOptions),
                new UTF8Encoding(false));
            _entries = normalized;
        }

        Interlocked.Increment(ref _version);
    }

    public static IReadOnlyList<string> Validate(
        string original,
        string translation)
    {
        if (string.IsNullOrWhiteSpace(original) ||
            string.IsNullOrWhiteSpace(translation))
        {
            return [];
        }

        var issues = new List<string>();

        foreach (var entry in GetEntries())
        {
            if (!entry.Required)
                continue;

            var comparison = entry.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (!original.Contains(entry.Source, comparison))
                continue;

            if (!translation.Contains(entry.Target, comparison))
            {
                issues.Add(
                    $"Глоссарий: «{entry.Source}» ожидает «{entry.Target}»");
            }
        }

        return issues;
    }

    public static IReadOnlyList<GlossaryEntry> Match(string original)
    {
        if (string.IsNullOrWhiteSpace(original))
            return [];

        return GetEntries()
            .Where(entry =>
            {
                var comparison = entry.CaseSensitive
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;
                return original.Contains(entry.Source, comparison);
            })
            .ToList();
    }

    private static IReadOnlyList<GlossaryEntry> LoadCore()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return [];

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

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
}
