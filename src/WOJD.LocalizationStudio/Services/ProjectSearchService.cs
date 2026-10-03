using System.IO;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record ProjectSearchResult(string FilePath, string FileName, int Index, string Namespace, string Key, string MatchField, string Preview, LocalizationEntry Entry);
public sealed record ProjectSearchResponse(List<ProjectSearchResult> Results, bool IsTruncated);
public static class ProjectSearchService
{
    public static ProjectSearchResponse Search(IEnumerable<LocalizationDocument> documents, string query, bool matchCase, bool exactMatch, bool useRegex)
        => SearchLocations(documents.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))), query, matchCase, exactMatch, useRegex);
    public static ProjectSearchResponse SearchLocations(IEnumerable<EntryLocation> rows, string query, bool matchCase, bool exact, bool useRegex, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new([], false);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var regex = useRegex ? new Regex(query, RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(250)) : null;
        var results = new List<ProjectSearchResult>();
        foreach (var location in rows)
        {
            cancellation.ThrowIfCancellationRequested(); var entry = location.Entry;
            foreach (var candidate in new[] { ("Namespace", entry.Namespace), ("Ключ", entry.Key), ("Оригинал", entry.Original), ("Перевод", entry.Translation) })
            {
                if (!(regex?.IsMatch(candidate.Item2) ?? (exact ? string.Equals(candidate.Item2, query, comparison) : candidate.Item2.Contains(query, comparison)))) continue;
                var text = candidate.Item2.Replace("\r\n", " ↵ ").Replace("\n", " ↵ ").Replace("\r", " ↵ ");
                results.Add(new(location.FilePath, Path.GetFileName(location.FilePath), entry.Index, entry.Namespace, entry.Key, candidate.Item1, text.Length > 220 ? text[..220] + "…" : text, entry));
                break;
            }
            if (results.Count >= 50000) return new(results, true);
        }
        return new(results, false);
    }
}