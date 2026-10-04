using System.IO;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class GlossaryTerm
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
    public bool Required { get; set; } = true;
    public string Note { get; set; } = string.Empty;
}

public sealed record GlossaryIssue(
    GlossaryTerm Term,
    string Message);

public static class GlossaryService
{
    private static readonly object Sync = new();
    private static IReadOnlyList<GlossaryTerm>? _terms;

    public static event EventHandler? Changed;

    public static string SettingsPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "glossary.json");

    public static IReadOnlyList<GlossaryTerm> GetTerms()
    {
        lock (Sync)
            return _terms ??= LoadCore();
    }

    public static void Save(IEnumerable<GlossaryTerm> terms)
    {
        var normalized = terms
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Source) &&
                !string.IsNullOrWhiteSpace(x.Target))
            .Select(x => new GlossaryTerm
            {
                Source = x.Source.Trim(),
                Target = x.Target.Trim(),
                CaseSensitive = x.CaseSensitive,
                Required = x.Required,
                Note = x.Note?.Trim() ?? string.Empty
            })
            .DistinctBy(
                x => $"{x.Source}\u001F{x.Target}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Source, StringComparer.CurrentCulture)
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
            _terms = normalized;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static IReadOnlyList<GlossaryIssue> Validate(
        string original,
        string translation)
    {
        if (string.IsNullOrWhiteSpace(original) ||
            string.IsNullOrWhiteSpace(translation))
        {
            return [];
        }

        var issues = new List<GlossaryIssue>();

        foreach (var term in GetTerms())
        {
            if (!term.Required)
                continue;

            var comparison = term.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (!original.Contains(term.Source, comparison))
                continue;

            if (translation.Contains(term.Target, comparison))
                continue;

            issues.Add(new GlossaryIssue(
                term,
                $"Глоссарий: для «{term.Source}» ожидается «{term.Target}»"));
        }

        return issues;
    }

    public static IReadOnlyList<(GlossaryTerm Term, int Occurrences, int Violations)> AnalyzeDocument(
        LocalizationDocument document,
        CancellationToken cancellationToken = default)
    {
        var result = new List<(GlossaryTerm, int, int)>();

        foreach (var term in GetTerms())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var comparison = term.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            var occurrences = 0;
            var violations = 0;

            foreach (var entry in document.Entries)
            {
                if (!entry.Original.Contains(term.Source, comparison))
                    continue;

                occurrences++;

                if (term.Required &&
                    !string.IsNullOrWhiteSpace(entry.Translation) &&
                    !entry.Translation.Contains(term.Target, comparison))
                {
                    violations++;
                }
            }

            result.Add((term, occurrences, violations));
        }

        return result;
    }

    public static void Reload()
    {
        lock (Sync)
            _terms = null;

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static IReadOnlyList<GlossaryTerm> LoadCore()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return [];

            return JsonSerializer.Deserialize<List<GlossaryTerm>>(
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
