using System.IO;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record ProjectSearchResult(
    string FilePath,
    string FileName,
    int Index,
    string Namespace,
    string Key,
    string MatchField,
    string Preview,
    LocalizationEntry Entry);

public sealed record ProjectSearchResponse(
    List<ProjectSearchResult> Results,
    bool IsTruncated);

public static class ProjectSearchService
{
    private const int MaxResults = 50000;

    public static ProjectSearchResponse Search(
        IEnumerable<LocalizationDocument> documents,
        string query,
        bool matchCase,
        bool exactMatch,
        bool useRegex)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ProjectSearchResponse([], false);

        var comparison =
            matchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

        Regex? regex = null;

        if (useRegex)
        {
            regex =
                new Regex(
                    query,
                    RegexOptions.CultureInvariant |
                    (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase),
                    TimeSpan.FromMilliseconds(750));
        }

        var results =
            new List<ProjectSearchResult>();

        var truncated = false;

        foreach (var document in documents)
        {
            var fileName =
                Path.GetFileName(document.FilePath);

            foreach (var entry in document.Entries)
            {
                if (!TryMatchField(
                        entry,
                        query,
                        comparison,
                        exactMatch,
                        regex,
                        out var field,
                        out var matchedText))
                {
                    continue;
                }

                results.Add(
                    new ProjectSearchResult(
                        document.FilePath,
                        fileName,
                        entry.Index,
                        entry.Namespace,
                        entry.Key,
                        field,
                        BuildPreview(matchedText),
                        entry));

                if (results.Count >= MaxResults)
                {
                    truncated = true;
                    return new ProjectSearchResponse(
                        results,
                        truncated);
                }
            }
        }

        return new ProjectSearchResponse(
            results,
            truncated);
    }

    private static bool TryMatchField(
        LocalizationEntry entry,
        string query,
        StringComparison comparison,
        bool exactMatch,
        Regex? regex,
        out string field,
        out string matchedText)
    {
        var candidates =
            new (string Field, string Text)[]
            {
                ("Namespace", entry.Namespace),
                ("Ключ", entry.Key),
                ("Оригинал", entry.Original),
                ("Перевод", entry.Translation)
            };

        foreach (var candidate in candidates)
        {
            if (!Matches(
                    candidate.Text,
                    query,
                    comparison,
                    exactMatch,
                    regex))
            {
                continue;
            }

            field = candidate.Field;
            matchedText = candidate.Text;
            return true;
        }

        field = string.Empty;
        matchedText = string.Empty;
        return false;
    }

    private static bool Matches(
        string text,
        string query,
        StringComparison comparison,
        bool exactMatch,
        Regex? regex)
    {
        if (regex is not null)
            return regex.IsMatch(text);

        if (exactMatch)
            return string.Equals(text, query, comparison);

        return text.Contains(query, comparison);
    }

    private static string BuildPreview(string text)
    {
        var normalized =
            text
                .Replace("\r\n", " ↵ ")
                .Replace("\n", " ↵ ")
                .Replace("\r", " ↵ ");

        const int maxLength = 220;

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "…";
    }
}
