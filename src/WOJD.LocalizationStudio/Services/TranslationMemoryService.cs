using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record TranslationMemorySuggestion(
    LocalizationEntry Entry,
    double Score,
    string Original,
    string Translation)
{
    public int Percent => (int)Math.Round(Score * 100);
}

public static class TranslationMemoryService
{
    private static readonly ConditionalWeakTable<LocalizationDocument, MemoryIndex>
        Indexes = new();

    private static readonly Regex TechnicalRegex =
        new(
            @"<\/?[^<>\r\n]+?>|\$\{[^{}\r\n]+\}|(?<!\$)\{[^{}\r\n]+\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc]",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    public static Task<IReadOnlyList<TranslationMemorySuggestion>> FindAsync(
        LocalizationDocument document,
        LocalizationEntry entry,
        int maxResults = 20,
        CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
        => Task.Run(
            () => Find(document, entry, maxResults, cancellationToken, progress),
            cancellationToken);

    public static IReadOnlyList<TranslationMemorySuggestion> Find(
        LocalizationDocument document,
        LocalizationEntry entry,
        int maxResults = 20,
        CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        var index = Indexes.GetValue(document, BuildIndex);
        var query = Normalize(entry.Original);

        if (query.Length == 0)
            return [];

        var grams = BuildGrams(query);
        var candidateHits = new Dictionary<int, int>();

        foreach (var gram in grams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!index.ByGram.TryGetValue(gram, out var ids))
                continue;

            foreach (var id in ids)
            {
                candidateHits[id] =
                    candidateHits.TryGetValue(id, out var count)
                        ? count + 1
                        : 1;
            }
        }

        var candidateIds = candidateHits
            .OrderByDescending(x => x.Value)
            .Take(1200)
            .Select(x => x.Key)
            .ToList();

        // Для очень коротких строк n-граммы могут ничего не дать.
        if (candidateIds.Count == 0)
        {
            candidateIds = Enumerable
                .Range(0, index.Items.Count)
                .Take(5000)
                .ToList();
        }

        var suggestions = new List<TranslationMemorySuggestion>();
        var processed = 0;

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = index.Items[id];
            processed++;

            if (ReferenceEquals(candidate.Entry, entry) ||
                string.IsNullOrWhiteSpace(candidate.Entry.Translation))
            {
                continue;
            }

            var score = Similarity(query, candidate.NormalizedOriginal);

            if (score < 0.35)
                continue;

            suggestions.Add(
                new TranslationMemorySuggestion(
                    candidate.Entry,
                    score,
                    candidate.Entry.Original,
                    candidate.Entry.Translation));

            if (processed % 100 == 0)
                progress?.Report(processed);
        }

        return suggestions
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Index)
            .Take(Math.Clamp(maxResults, 1, 100))
            .ToList();
    }

    public static void Invalidate(LocalizationDocument document)
        => Indexes.Remove(document);

    private static MemoryIndex BuildIndex(LocalizationDocument document)
    {
        var items = new List<IndexedEntry>();
        var byGram = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var entry in document.Entries)
        {
            var normalized = Normalize(entry.Original);
            if (normalized.Length == 0)
                continue;

            var id = items.Count;
            items.Add(new IndexedEntry(entry, normalized));

            foreach (var gram in BuildGrams(normalized))
            {
                if (!byGram.TryGetValue(gram, out var list))
                {
                    list = [];
                    byGram[gram] = list;
                }

                if (list.Count == 0 || list[^1] != id)
                    list.Add(id);
            }
        }

        return new MemoryIndex(items, byGram);
    }

    private static string Normalize(string value)
    {
        value = TechnicalRegex.Replace(value ?? string.Empty, " ");
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().Trim();
    }

    private static HashSet<string> BuildGrams(string value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var compact = value.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (compact.Length <= 2)
        {
            if (compact.Length > 0)
                result.Add(compact);
            return result;
        }

        for (var i = 0; i <= compact.Length - 3; i++)
            result.Add(compact.Substring(i, 3));

        return result;
    }

    private static double Similarity(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return 1.0;

        var a = BuildGrams(left);
        var b = BuildGrams(right);

        if (a.Count == 0 || b.Count == 0)
            return 0;

        var intersection = a.Count(b.Contains);
        var dice = (2.0 * intersection) / (a.Count + b.Count);

        // Небольшой бонус за совпадающее начало — хорошо работает для коротких UI-строк.
        var prefix = 0;
        var limit = Math.Min(left.Length, right.Length);
        while (prefix < limit && left[prefix] == right[prefix])
            prefix++;

        var prefixBonus = limit == 0
            ? 0
            : Math.Min(0.12, (double)prefix / limit * 0.12);

        return Math.Min(1.0, dice + prefixBonus);
    }

    private sealed record IndexedEntry(
        LocalizationEntry Entry,
        string NormalizedOriginal);

    private sealed record MemoryIndex(
        IReadOnlyList<IndexedEntry> Items,
        IReadOnlyDictionary<string, List<int>> ByGram);
}
