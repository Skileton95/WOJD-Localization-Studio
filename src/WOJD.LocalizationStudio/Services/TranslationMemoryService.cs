using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record TranslationMemorySuggestion(
    int Score,
    string Source,
    string Translation,
    string Namespace,
    string Key,
    int Index);

public sealed partial class TranslationMemoryIndex
{
    private readonly Dictionary<string, List<LocalizationEntry>> _exact;
    private readonly Dictionary<string, List<LocalizationEntry>> _buckets;

    private TranslationMemoryIndex(
        Dictionary<string, List<LocalizationEntry>> exact,
        Dictionary<string, List<LocalizationEntry>> buckets)
    {
        _exact = exact;
        _buckets = buckets;
    }

    public static TranslationMemoryIndex Build(
        IReadOnlyList<LocalizationEntry> entries,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var exact = new Dictionary<string, List<LocalizationEntry>>(StringComparer.Ordinal);
        var buckets = new Dictionary<string, List<LocalizationEntry>>(StringComparer.Ordinal);

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry.Original) ||
                string.IsNullOrWhiteSpace(entry.Translation))
            {
                continue;
            }

            var normalized = Normalize(entry.Original);
            if (normalized.Length == 0)
                continue;

            if (!exact.TryGetValue(normalized, out var exactList))
                exact[normalized] = exactList = [];
            exactList.Add(entry);

            foreach (var bucketKey in GetBucketKeys(normalized))
            {
                if (!buckets.TryGetValue(bucketKey, out var bucket))
                    buckets[bucketKey] = bucket = [];

                if (bucket.Count < 8000)
                    bucket.Add(entry);
            }

            if (i % 5000 == 0)
                progress?.Report((i, entries.Count));
        }

        progress?.Report((entries.Count, entries.Count));
        return new TranslationMemoryIndex(exact, buckets);
    }

    public IReadOnlyList<TranslationMemorySuggestion> Search(
        LocalizationEntry query,
        int limit = 8)
    {
        if (string.IsNullOrWhiteSpace(query.Original))
            return [];

        var source = Normalize(query.Original);
        if (source.Length == 0)
            return [];

        var candidates = new HashSet<LocalizationEntry>();

        if (_exact.TryGetValue(source, out var exactMatches))
        {
            foreach (var entry in exactMatches)
                candidates.Add(entry);
        }

        foreach (var bucketKey in GetBucketKeys(source))
        {
            if (!_buckets.TryGetValue(bucketKey, out var bucket))
                continue;

            foreach (var entry in bucket.Take(2500))
                candidates.Add(entry);
        }

        return candidates
            .Where(x => !ReferenceEquals(x, query) && !string.IsNullOrWhiteSpace(x.Translation))
            .Select(x =>
            {
                var normalizedCandidate = Normalize(x.Original);
                var score = string.Equals(source, normalizedCandidate, StringComparison.Ordinal)
                    ? 100
                    : Similarity(source, normalizedCandidate);
                return new TranslationMemorySuggestion(
                    score,
                    x.Original,
                    x.Translation,
                    x.Namespace,
                    x.Key,
                    x.Index);
            })
            .Where(x => x.Score >= 45)
            .GroupBy(x => x.Translation, StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(y => y.Score).First())
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Translation.Length)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private static int Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return 0;

        var leftGrams = BuildNGrams(left);
        var rightGrams = BuildNGrams(right);
        if (leftGrams.Count == 0 || rightGrams.Count == 0)
            return 0;

        var intersection = leftGrams.Count(x => rightGrams.Contains(x));
        var union = leftGrams.Count + rightGrams.Count - intersection;
        var jaccard = union == 0 ? 0 : (double)intersection / union;
        var lengthRatio = (double)Math.Min(left.Length, right.Length) / Math.Max(left.Length, right.Length);
        return (int)Math.Round((jaccard * 0.8 + lengthRatio * 0.2) * 100);
    }

    private static HashSet<string> BuildNGrams(string value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (value.Length <= 2)
        {
            result.Add(value);
            return result;
        }

        for (var i = 0; i <= value.Length - 2; i++)
            result.Add(value.Substring(i, 2));
        return result;
    }

    private static IEnumerable<string> GetBucketKeys(string normalized)
    {
        var bucket = normalized.Length / 12;
        var prefix = normalized.Length >= 2 ? normalized[..2] : normalized;
        yield return prefix + ":" + bucket;

        if (normalized.Length >= 4)
            yield return normalized[^2..] + ":" + bucket;
    }

    private static string Normalize(string value)
    {
        var text = TokenRegex().Replace(value ?? string.Empty, string.Empty);
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch) && !char.IsPunctuation(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"(<\/?[^<>\r\n]+?>|\$\{[^{}\r\n]+\}|(?<!\$)\{[^{}\r\n]+\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc])",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}

public static class TranslationMemoryService
{
    private static readonly object Sync = new();
    private static LocalizationDocument? _document;
    private static TranslationMemoryIndex? _index;

    public static async Task<TranslationMemoryIndex> GetOrBuildAsync(
        LocalizationDocument document,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        lock (Sync)
        {
            if (ReferenceEquals(_document, document) && _index is not null)
                return _index;
        }

        var built = await Task.Run(
            () => TranslationMemoryIndex.Build(document.Entries, progress, cancellationToken),
            cancellationToken);

        lock (Sync)
        {
            _document = document;
            _index = built;
        }

        return built;
    }

    public static void Invalidate(LocalizationDocument? document = null)
    {
        lock (Sync)
        {
            if (document is not null && !ReferenceEquals(document, _document))
                return;
            _document = null;
            _index = null;
        }
    }
}
