using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record ConsistencyVariant(
    string Translation,
    int Count,
    IReadOnlyList<int> ExampleIndexes);

public sealed record ConsistencyIssue(
    string Original,
    int TotalOccurrences,
    IReadOnlyList<ConsistencyVariant> Variants);

public static class ConsistencyService
{
    public static Task<IReadOnlyList<ConsistencyIssue>> AnalyzeAsync(
        LocalizationDocument document,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ConsistencyIssue>>(
            () => Analyze(document, progress, cancellationToken),
            cancellationToken);

    public static IReadOnlyList<ConsistencyIssue> Analyze(
        LocalizationDocument document,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var groups = new Dictionary<string, Dictionary<string, VariantAccumulator>>(StringComparer.Ordinal);
        var entries = document.Entries;

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];

            if (!string.IsNullOrWhiteSpace(entry.Original) &&
                !string.IsNullOrWhiteSpace(entry.Translation))
            {
                var source = entry.Original.Trim();
                var target = entry.Translation.Trim();

                if (!groups.TryGetValue(source, out var variants))
                    groups[source] = variants = new Dictionary<string, VariantAccumulator>(StringComparer.Ordinal);

                if (!variants.TryGetValue(target, out var accumulator))
                    variants[target] = accumulator = new VariantAccumulator();

                accumulator.Count++;
                if (accumulator.ExampleIndexes.Count < 8)
                    accumulator.ExampleIndexes.Add(entry.Index);
            }

            if (i % 5000 == 0)
                progress?.Report((i, entries.Count));
        }

        progress?.Report((entries.Count, entries.Count));

        return groups
            .Where(x => x.Value.Count > 1)
            .Select(x => new ConsistencyIssue(
                x.Key,
                x.Value.Values.Sum(v => v.Count),
                x.Value
                    .Select(v => new ConsistencyVariant(
                        v.Key,
                        v.Value.Count,
                        v.Value.ExampleIndexes))
                    .OrderByDescending(v => v.Count)
                    .ThenBy(v => v.Translation, StringComparer.Ordinal)
                    .ToList()))
            .OrderByDescending(x => x.TotalOccurrences)
            .ThenBy(x => x.Original, StringComparer.Ordinal)
            .ToList();
    }

    private sealed class VariantAccumulator
    {
        public int Count { get; set; }
        public List<int> ExampleIndexes { get; } = [];
    }
}
