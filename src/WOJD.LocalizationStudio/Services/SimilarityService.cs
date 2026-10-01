using System.Text;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class SimilarityService
{
    public static IReadOnlyList<SimilarityMatch> FindSourceMatches(
        LocalizationEntry current,
        IEnumerable<LocalizationDocument> documents,
        int limit = 15)
    {
        if (string.IsNullOrWhiteSpace(current.Source))
            return Array.Empty<SimilarityMatch>();

        var normalizedCurrent = Normalize(current.Source);
        if (normalizedCurrent.Length < 2)
            return Array.Empty<SimilarityMatch>();

        return documents
            .SelectMany(document => document.Entries.Select(entry => new { document, entry }))
            .Where(item =>
                !ReferenceEquals(item.entry, current) &&
                !string.IsNullOrWhiteSpace(item.entry.Source) &&
                !string.IsNullOrWhiteSpace(item.entry.Translation))
            .Select(item => new SimilarityMatch(
                item.document,
                item.entry,
                DiceCoefficient(normalizedCurrent, Normalize(item.entry.Source)),
                "Оригинал"))
            .Where(match => match.Similarity >= 0.65 && match.Similarity < 0.999999)
            .OrderByDescending(match => match.Similarity)
            .ThenBy(match => match.Entry.Source.Length)
            .Take(limit)
            .ToList();
    }

    public static IReadOnlyList<SimilarityMatch> FindTranslationMatches(
        LocalizationEntry current,
        IEnumerable<LocalizationDocument> documents,
        int limit = 15)
    {
        if (string.IsNullOrWhiteSpace(current.Translation))
            return Array.Empty<SimilarityMatch>();

        var currentTokens = Tokenize(current.Translation);
        if (currentTokens.Count == 0)
            return Array.Empty<SimilarityMatch>();

        return documents
            .SelectMany(document => document.Entries.Select(entry => new { document, entry }))
            .Where(item =>
                !ReferenceEquals(item.entry, current) &&
                !string.IsNullOrWhiteSpace(item.entry.Translation))
            .Select(item => new SimilarityMatch(
                item.document,
                item.entry,
                Jaccard(currentTokens, Tokenize(item.entry.Translation)),
                "Перевод"))
            .Where(match => match.Similarity >= 0.50 && match.Similarity < 0.999999)
            .OrderByDescending(match => match.Similarity)
            .ThenBy(match => match.Entry.Translation.Length)
            .Take(limit)
            .ToList();
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
                continue;

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static double DiceCoefficient(string left, string right)
    {
        if (left == right)
            return 1.0;

        if (left.Length < 2 || right.Length < 2)
            return 0;

        var leftPairs = BuildBigrams(left);
        var rightPairs = BuildBigrams(right);

        var intersection = 0;
        var counts = new Dictionary<string, int>(leftPairs, StringComparer.Ordinal);

        foreach (var pair in rightPairs)
        {
            if (!counts.TryGetValue(pair.Key, out var available) || available <= 0)
                continue;

            intersection += Math.Min(available, pair.Value);
        }

        var leftTotal = leftPairs.Values.Sum();
        var rightTotal = rightPairs.Values.Sum();
        return leftTotal + rightTotal == 0
            ? 0
            : 2.0 * intersection / (leftTotal + rightTotal);
    }

    private static Dictionary<string, int> BuildBigrams(string value)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < value.Length - 1; i++)
        {
            var pair = value.Substring(i, 2);
            result.TryGetValue(pair, out var count);
            result[pair] = count + 1;
        }
        return result;
    }

    private static HashSet<string> Tokenize(string value) =>
        value
            .ToLowerInvariant()
            .Split(
                [' ', '\t', '\r', '\n', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}', '«', '»', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.Ordinal);

    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return 0;

        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }
}

public sealed record SimilarityMatch(
    LocalizationDocument Document,
    LocalizationEntry Entry,
    double Similarity,
    string Mode)
{
    public string PercentText => $"{Similarity:P0}";
}
