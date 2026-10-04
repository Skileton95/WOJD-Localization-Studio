using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record ConsistencyVariant(
    string Translation,
    int Count,
    LocalizationEntry Example);

public sealed record ConsistencyIssue(
    string Original,
    int TotalOccurrences,
    IReadOnlyList<ConsistencyVariant> Variants)
{
    public int VariantCount => Variants.Count;
}

public static class ConsistencyService
{
    public static IReadOnlyList<ConsistencyIssue> Analyze(
        LocalizationDocument document,
        CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        var groups = new Dictionary<string, List<LocalizationEntry>>(
            StringComparer.Ordinal);

        var total = document.Entries.Count;
        var processed = 0;

        foreach (var entry in document.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(entry.Original) &&
                !string.IsNullOrWhiteSpace(entry.Translation))
            {
                if (!groups.TryGetValue(entry.Original, out var list))
                {
                    list = [];
                    groups[entry.Original] = list;
                }

                list.Add(entry);
            }

            processed++;
            if (processed % 5000 == 0 || processed == total)
                progress?.Report(processed);
        }

        return groups
            .Where(pair => pair.Value.Count > 1)
            .Select(pair =>
            {
                var variants = pair.Value
                    .GroupBy(x => NormalizeTranslation(x.Translation), StringComparer.OrdinalIgnoreCase)
                    .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                    .Select(group =>
                        new ConsistencyVariant(
                            group.First().Translation,
                            group.Count(),
                            group.First()))
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Translation, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                return new ConsistencyIssue(
                    pair.Key,
                    pair.Value.Count,
                    variants);
            })
            .Where(x => x.Variants.Count > 1)
            .OrderByDescending(x => x.TotalOccurrences)
            .ThenByDescending(x => x.VariantCount)
            .ToList();
    }

    private static string NormalizeTranslation(string value)
        => string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));
}
