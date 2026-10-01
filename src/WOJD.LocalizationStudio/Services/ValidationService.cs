using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class ValidationService
{
    private static readonly Regex TokenRegex = new(
        @"\{\d+(?:[^{}]*)?\}|%(?:\d+\$)?[sd]|\\n|\r?\n|<\/?[A-Za-z][^>]*?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void Validate(LocalizationEntry entry)
    {
        entry.ValidationSummary = BuildSummary(entry.Source, entry.Translation);
    }

    public static void ValidateAll(IEnumerable<LocalizationEntry> entries)
    {
        foreach (var entry in entries)
            Validate(entry);
    }

    public static string AutoFixTechnicalTokens(string source, string translation)
    {
        if (string.IsNullOrEmpty(translation))
            return translation;

        var requiredCounts = TokenRegex.Matches(source ?? string.Empty)
            .Select(match => NormalizeToken(match.Value))
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var keptCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        var cleaned = TokenRegex.Replace(translation, match =>
        {
            var normalized = NormalizeToken(match.Value);
            requiredCounts.TryGetValue(normalized, out var required);

            keptCounts.TryGetValue(normalized, out var kept);
            if (kept >= required)
                return string.Empty;

            keptCounts[normalized] = kept + 1;
            return match.Value;
        });

        var missingRawTokens = new List<string>();
        var existingCounts = TokenRegex.Matches(cleaned)
            .Select(match => NormalizeToken(match.Value))
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var consumedSourceCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match match in TokenRegex.Matches(source ?? string.Empty))
        {
            var normalized = NormalizeToken(match.Value);
            consumedSourceCounts.TryGetValue(normalized, out var sourceSeen);
            existingCounts.TryGetValue(normalized, out var existing);

            sourceSeen++;
            consumedSourceCounts[normalized] = sourceSeen;

            if (sourceSeen > existing)
                missingRawTokens.Add(match.Value);
        }

        if (missingRawTokens.Count == 0)
            return cleaned;

        var separator = cleaned.Length == 0 || char.IsWhiteSpace(cleaned[^1]) ? string.Empty : " ";
        return cleaned + separator + string.Join(" ", missingRawTokens);
    }

    public static string BuildSummary(string source, string translation)
    {
        if (string.IsNullOrWhiteSpace(translation)) return string.Empty;

        var sourceTokens = ExtractTokens(source);
        var translationTokens = ExtractTokens(translation);

        if (sourceTokens.Count == 0 && translationTokens.Count == 0)
            return string.Empty;

        var sourceCounts = sourceTokens
            .GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var translationCounts = translationTokens
            .GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var missing = new List<string>();
        var extra = new List<string>();

        foreach (var pair in sourceCounts)
        {
            translationCounts.TryGetValue(pair.Key, out var translatedCount);
            var delta = pair.Value - translatedCount;
            if (delta > 0)
                missing.Add(FormatTokenCount(pair.Key, delta));
        }

        foreach (var pair in translationCounts)
        {
            sourceCounts.TryGetValue(pair.Key, out var sourceCount);
            var delta = pair.Value - sourceCount;
            if (delta > 0)
                extra.Add(FormatTokenCount(pair.Key, delta));
        }

        if (missing.Count == 0 && extra.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        if (missing.Count > 0) parts.Add($"Потеряно: {string.Join(", ", missing)}");
        if (extra.Count > 0) parts.Add($"Лишнее: {string.Join(", ", extra)}");
        return string.Join(". ", parts);
    }

    public static IReadOnlyList<string> ExtractTokens(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Array.Empty<string>();

        return TokenRegex.Matches(value)
            .Select(m => NormalizeToken(m.Value))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    private static string NormalizeToken(string token) =>
        token is "\r\n" or "\n" or "\\n" ? "\\n" : token;

    private static string FormatTokenCount(string token, int count) =>
        count == 1 ? token : $"{token} ×{count}";
}
