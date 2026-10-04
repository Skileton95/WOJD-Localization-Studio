using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record ConsistencyVariant(
    string Translation,
    int Count,
    IReadOnlyList<LocalizationEntry> Entries);

public sealed record ConsistencyIssue(
    string Original,
    int Occurrences,
    IReadOnlyList<ConsistencyVariant> Variants)
{
    public int DistinctTranslations => Variants.Count;
}

public static class ConsistencyService
{
    public static IReadOnlyList<ConsistencyIssue> Analyze(
        LocalizationDocument document,
        int minimumOccurrences = 2,
        CancellationToken cancellationToken = default)
    {
        minimumOccurrences = Math.Max(2, minimumOccurrences);

        var result = new List<ConsistencyIssue>();

        foreach (var sourceGroup in document.Entries
                     .Where(x =>
                         !string.IsNullOrWhiteSpace(x.Original) &&
                         !string.IsNullOrWhiteSpace(x.Translation))
                     .GroupBy(x => x.Original.Trim(), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entries = sourceGroup.ToList();
            if (entries.Count < minimumOccurrences)
                continue;

            var variants = entries
                .GroupBy(x => NormalizeTranslation(x.Translation), StringComparer.OrdinalIgnoreCase)
                .Select(group => new ConsistencyVariant(
                    group.First().Translation.Trim(),
                    group.Count(),
                    group.OrderBy(x => x.Index).ToList()))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Translation, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (variants.Count <= 1)
                continue;

            result.Add(new ConsistencyIssue(
                sourceGroup.Key,
                entries.Count,
                variants));
        }

        return result
            .OrderByDescending(x => x.Occurrences)
            .ThenByDescending(x => x.DistinctTranslations)
            .ThenBy(x => x.Original, StringComparer.CurrentCulture)
            .ToList();
    }

    public static Task<IReadOnlyList<ConsistencyIssue>> AnalyzeAsync(
        LocalizationDocument document,
        IProgress<(int Current, int Total, string Text)>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ConsistencyIssue>>(
            () =>
            {
                var groups = document.Entries
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Original) &&
                        !string.IsNullOrWhiteSpace(x.Translation))
                    .GroupBy(x => x.Original.Trim(), StringComparer.Ordinal)
                    .ToList();

                var result = new List<ConsistencyIssue>();

                for (var i = 0; i < groups.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var group = groups[i];
                    var entries = group.ToList();

                    if (entries.Count > 1)
                    {
                        var variants = entries
                            .GroupBy(x => NormalizeTranslation(x.Translation), StringComparer.OrdinalIgnoreCase)
                            .Select(x => new ConsistencyVariant(
                                x.First().Translation.Trim(),
                                x.Count(),
                                x.OrderBy(e => e.Index).ToList()))
                            .OrderByDescending(x => x.Count)
                            .ToList();

                        if (variants.Count > 1)
                            result.Add(new ConsistencyIssue(group.Key, entries.Count, variants));
                    }

                    if (i % 250 == 0 || i + 1 == groups.Count)
                    {
                        progress?.Report((
                            i + 1,
                            groups.Count,
                            $"Проверка согласованности: {i + 1:N0}/{groups.Count:N0}"));
                    }
                }

                return result
                    .OrderByDescending(x => x.Occurrences)
                    .ThenByDescending(x => x.DistinctTranslations)
                    .ThenBy(x => x.Original, StringComparer.CurrentCulture)
                    .ToList();
            },
            cancellationToken);

    private static string NormalizeTranslation(string value)
        => string.Join(
            " ",
            (value ?? string.Empty)
                .Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
