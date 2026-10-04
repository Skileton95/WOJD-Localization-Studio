using System.Collections.Concurrent;
using System.Text;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record TranslationMemoryMatch(
    LocalizationEntry Entry,
    double Score,
    string Source,
    string Translation);

public static class TranslationMemoryService
{
    private static readonly ConcurrentDictionary<string, string[]> GramCache =
        new(StringComparer.Ordinal);

    public static IReadOnlyList<TranslationMemoryMatch> FindMatches(
        LocalizationDocument document,
        LocalizationEntry query,
        int limit = 8,
        double threshold = 0.58)
    {
        if (document is null || query is null || string.IsNullOrWhiteSpace(query.Original))
            return [];

        limit = Math.Clamp(limit, 1, 50);
        threshold = Math.Clamp(threshold, 0.1, 1.0);

        var queryNormalized = Normalize(query.Original);
        var queryGrams = GetGrams(queryNormalized);
        var candidates = new List<TranslationMemoryMatch>();

        foreach (var entry in document.Entries)
        {
            if (ReferenceEquals(entry, query) ||
                string.IsNullOrWhiteSpace(entry.Original) ||
                string.IsNullOrWhiteSpace(entry.Translation))
            {
                continue;
            }

            var source = Normalize(entry.Original);
            if (source.Length == 0)
                continue;

            double score;

            if (string.Equals(source, queryNormalized, StringComparison.Ordinal))
            {
                score = 1.0;
            }
            else
            {
                var quickRatio =
                    (double)Math.Min(source.Length, queryNormalized.Length) /
                    Math.Max(source.Length, queryNormalized.Length);

                if (quickRatio < threshold * 0.55)
                    continue;

                score = Dice(queryGrams, GetGrams(source));
            }

            if (score < threshold)
                continue;

            candidates.Add(new TranslationMemoryMatch(
                entry,
                score,
                entry.Original,
                entry.Translation));
        }

        return candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Index)
            .Take(limit)
            .ToList();
    }

    public static Task<IReadOnlyList<TranslationMemoryMatch>> FindMatchesAsync(
        LocalizationDocument document,
        LocalizationEntry query,
        int limit,
        double threshold,
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<TranslationMemoryMatch>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return FindMatches(document, query, limit, threshold);
            },
            cancellationToken);

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;

        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                    builder.Append(' ');

                previousWhitespace = true;
                continue;
            }

            previousWhitespace = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string[] GetGrams(string value)
        => GramCache.GetOrAdd(value, static text => BuildGrams(text));

    private static string[] BuildGrams(string value)
    {
        if (value.Length <= 2)
            return value.Length == 0 ? [] : [value];

        var result = new string[value.Length - 2];
        for (var i = 0; i < result.Length; i++)
            result[i] = value.Substring(i, 3);

        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    private static double Dice(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return 0;

        var i = 0;
        var j = 0;
        var matches = 0;

        while (i < left.Count && j < right.Count)
        {
            var compare = string.CompareOrdinal(left[i], right[j]);

            if (compare == 0)
            {
                matches++;
                i++;
                j++;
            }
            else if (compare < 0)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return 2d * matches / (left.Count + right.Count);
    }
}
