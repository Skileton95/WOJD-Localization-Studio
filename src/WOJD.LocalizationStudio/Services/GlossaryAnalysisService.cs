using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class GlossaryAnalysisService
{
    private static readonly Regex HanSequenceRegex = new(
        @"[㐀-䶿一-鿿豈-﫿]{2,12}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<GlossaryCandidate> FindCandidates(
        LocalizationDocument document,
        IEnumerable<GlossaryEntry> existingEntries,
        int limit = 250)
    {
        var existing = existingEntries
            .Select(entry => entry.Source)
            .ToHashSet(StringComparer.Ordinal);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var rowsByCandidate = new Dictionary<string, HashSet<LocalizationEntry>>(StringComparer.Ordinal);

        foreach (var entry in document.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Source))
                continue;

            var lineCandidates = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in HanSequenceRegex.Matches(entry.Source))
            {
                var sequence = match.Value;

                if (sequence.Length <= 6)
                    lineCandidates.Add(sequence);

                var maxLength = Math.Min(6, sequence.Length);
                for (var length = 2; length <= maxLength; length++)
                {
                    for (var start = 0; start <= sequence.Length - length; start++)
                        lineCandidates.Add(sequence.Substring(start, length));
                }
            }

            foreach (var candidate in lineCandidates)
            {
                if (existing.Contains(candidate))
                    continue;

                counts.TryGetValue(candidate, out var count);
                counts[candidate] = count + 1;

                if (!rowsByCandidate.TryGetValue(candidate, out var rows))
                {
                    rows = [];
                    rowsByCandidate[candidate] = rows;
                }

                rows.Add(entry);
            }
        }

        return counts
            .Where(pair => pair.Value >= 3)
            .Select(pair =>
            {
                var rows = rowsByCandidate[pair.Key].ToList();

                var exactTranslations = document.Entries
                    .Where(entry =>
                        string.Equals(entry.Source, pair.Key, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(entry.Translation))
                    .GroupBy(entry => entry.Translation, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .Select(group => $"{group.Key} ({group.Count()})")
                    .Take(5)
                    .ToList();

                var examples = rows
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Translation))
                    .Take(3)
                    .Select(entry => $"{entry.Key}: {entry.Translation}")
                    .ToList();

                var suggested = document.Entries
                    .Where(entry =>
                        string.Equals(entry.Source, pair.Key, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(entry.Translation))
                    .GroupBy(entry => entry.Translation, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .Select(group => group.Key)
                    .FirstOrDefault() ?? string.Empty;

                return new GlossaryCandidate(
                    pair.Key,
                    pair.Value,
                    exactTranslations.Count,
                    string.Join(" | ", exactTranslations),
                    string.Join(" | ", examples),
                    suggested);
            })
            .OrderByDescending(candidate => candidate.Occurrences)
            .ThenByDescending(candidate => candidate.Source.Length)
            .Take(limit)
            .ToList();
    }
}

public sealed record GlossaryCandidate(
    string Source,
    int Occurrences,
    int TranslationVariantCount,
    string TranslationVariants,
    string Examples,
    string SuggestedTranslation);
