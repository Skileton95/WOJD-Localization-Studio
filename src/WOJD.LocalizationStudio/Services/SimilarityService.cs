using System.Text;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class SimilarityService
{
    public static IReadOnlyList<SimilarityMatch> FindSourceMatches(
        LocalizationEntry current,
        IEnumerable<LocalizationDocument> documents,
        IEnumerable<TranslationMemoryEntry> memoryEntries,
        int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(current.Source))
            return Array.Empty<SimilarityMatch>();

        var normalizedCurrent = Normalize(current.Source);
        if (normalizedCurrent.Length < 2)
            return Array.Empty<SimilarityMatch>();

        var candidates = new List<SimilarityMatch>();

        foreach (var document in documents)
        {
            foreach (var entry in document.Entries)
            {
                if (ReferenceEquals(entry, current) ||
                    string.IsNullOrWhiteSpace(entry.Source) ||
                    string.IsNullOrWhiteSpace(entry.Translation))
                    continue;

                var similarity = DiceCoefficient(
                    normalizedCurrent,
                    Normalize(entry.Source));

                if (similarity < 0.65 || similarity >= 0.999999)
                    continue;

                candidates.Add(new SimilarityMatch(
                    document.FileName,
                    entry.Namespace,
                    entry.Key,
                    entry.Source,
                    entry.Translation,
                    similarity,
                    "Открытый файл",
                    false));
            }
        }

        foreach (var entry in memoryEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Source) ||
                string.IsNullOrWhiteSpace(entry.Translation))
                continue;

            var similarity = DiceCoefficient(
                normalizedCurrent,
                Normalize(entry.Source));

            if (similarity < 0.65)
                continue;

            candidates.Add(new SimilarityMatch(
                string.IsNullOrWhiteSpace(entry.FileName)
                    ? "Память переводов"
                    : entry.FileName,
                entry.Namespace,
                entry.Key,
                entry.Source,
                entry.Translation,
                similarity,
                "Память",
                true));
        }

        return candidates
            .GroupBy(
                item => $"{item.Source}\u001f{item.Translation}",
                StringComparer.Ordinal)
            .Select(group =>
                group
                    .OrderByDescending(item => item.IsPersistentMemory)
                    .ThenByDescending(item => item.Similarity)
                    .First())
            .OrderByDescending(match => match.Similarity)
            .ThenByDescending(match => match.IsPersistentMemory)
            .ThenBy(match => match.Source.Length)
            .Take(limit)
            .ToList();
    }

    public static IReadOnlyList<SimilarityMatch> FindTranslationMatches(
        LocalizationEntry current,
        IEnumerable<LocalizationDocument> documents,
        IEnumerable<TranslationMemoryEntry> memoryEntries,
        int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(current.Translation))
            return Array.Empty<SimilarityMatch>();

        var currentTokens = Tokenize(current.Translation);
        if (currentTokens.Count == 0)
            return Array.Empty<SimilarityMatch>();

        var candidates = new List<SimilarityMatch>();

        foreach (var document in documents)
        {
            foreach (var entry in document.Entries)
            {
                if (ReferenceEquals(entry, current) ||
                    string.IsNullOrWhiteSpace(entry.Translation))
                    continue;

                var similarity = Jaccard(
                    currentTokens,
                    Tokenize(entry.Translation));

                if (similarity < 0.50 || similarity >= 0.999999)
                    continue;

                candidates.Add(new SimilarityMatch(
                    document.FileName,
                    entry.Namespace,
                    entry.Key,
                    entry.Source,
                    entry.Translation,
                    similarity,
                    "Открытый файл",
                    false));
            }
        }

        foreach (var entry in memoryEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Translation))
                continue;

            var similarity = Jaccard(
                currentTokens,
                Tokenize(entry.Translation));

            if (similarity < 0.50)
                continue;

            candidates.Add(new SimilarityMatch(
                string.IsNullOrWhiteSpace(entry.FileName)
                    ? "Память переводов"
                    : entry.FileName,
                entry.Namespace,
                entry.Key,
                entry.Source,
                entry.Translation,
                similarity,
                "Память",
                true));
        }

        return candidates
            .GroupBy(
                item => $"{item.Source}\u001f{item.Translation}",
                StringComparer.Ordinal)
            .Select(group =>
                group
                    .OrderByDescending(item => item.IsPersistentMemory)
                    .ThenByDescending(item => item.Similarity)
                    .First())
            .OrderByDescending(match => match.Similarity)
            .ThenByDescending(match => match.IsPersistentMemory)
            .ThenBy(match => match.Translation.Length)
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

        foreach (var pair in rightPairs)
        {
            if (!leftPairs.TryGetValue(pair.Key, out var leftCount))
                continue;

            intersection += Math.Min(leftCount, pair.Value);
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
                [' ', '\t', '\r', '\n', ',', '.', '!', '?', ';', ':',
                 '(', ')', '[', ']', '{', '}', '«', '»', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.Ordinal);

    private static double Jaccard(
        HashSet<string> left,
        HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return 0;

        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;

        return union == 0
            ? 0
            : (double)intersection / union;
    }
}

public sealed record SimilarityMatch(
    string FileName,
    string Namespace,
    string Key,
    string Source,
    string Translation,
    double Similarity,
    string Origin,
    bool IsPersistentMemory)
{
    public string PercentText => $"{Similarity:P0}";
    public string OriginText => IsPersistentMemory ? "Память" : Origin;
}
