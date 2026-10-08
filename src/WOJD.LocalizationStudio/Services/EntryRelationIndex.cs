using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

/// <summary>
/// Immutable lookup index for relationships that are based on stable localization
/// metadata (Key / Namespace / Original). Building it once avoids rescanning very
/// large documents whenever the selected row changes.
/// </summary>
public sealed class EntryRelationIndex
{
    private static readonly ConditionalWeakTable<LocalizationDocument, EntryRelationIndex> Cache = new();
    private static readonly Regex NumericFamilyPattern = new(
        @"^(?<family>[\p{L}]*\d+(?:_\d+)+)(?=$|[-.:/|])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumericSeriesPattern = new(
        @"^(?<series>.+)_\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, List<LocalizationEntry>> _byOriginal =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<LocalizationEntry>> _byFamily =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LocalizationEntry>> _bySeries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LocalizationEntry, int> _positions = new();
    private readonly Dictionary<LocalizationEntry, string> _families = new();
    private readonly HashSet<LocalizationEntry> _groupStarts = [];

    private EntryRelationIndex(LocalizationDocument document)
    {
        Document = document;
        Build(document.Entries);
    }

    public LocalizationDocument Document { get; }

    public static EntryRelationIndex For(LocalizationDocument document)
        => Cache.GetValue(document, static value => new EntryRelationIndex(value));

    public int GetPosition(LocalizationEntry entry)
        => _positions.TryGetValue(entry, out var value) ? value : -1;

    public string GetFamily(LocalizationEntry entry)
        => _families.TryGetValue(entry, out var value) ? value : GetKeyFamily(entry.Key);

    public bool StartsVisualGroup(LocalizationEntry entry)
        => _groupStarts.Contains(entry);

    public IReadOnlyList<LocalizationEntry> GetSameOriginal(LocalizationEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Original))
            return [];

        return _byOriginal.TryGetValue(entry.Original, out var values)
            ? values
            : [];
    }

    public IReadOnlyList<LocalizationEntry> GetSameFamily(LocalizationEntry entry)
    {
        var family = GetFamily(entry);
        if (string.IsNullOrEmpty(family))
            return [];

        return _byFamily.TryGetValue(family, out var values)
            ? values
            : [];
    }

    public IReadOnlyList<LocalizationEntry> GetRelated(LocalizationEntry entry, int maxCount = 32)
    {
        if (maxCount <= 0)
            return [];

        var result = new HashSet<LocalizationEntry>();

        AddRange(result, GetSameFamily(entry), entry);
        AddRange(result, GetSameOriginal(entry), entry);

        var family = GetFamily(entry);
        var series = GetSeriesFamily(family);
        if (!string.IsNullOrEmpty(series) &&
            _bySeries.TryGetValue(series, out var seriesEntries))
        {
            AddRange(result, seriesEntries, entry);
        }

        return result
            .OrderBy(x => GetPosition(x))
            .Take(maxCount)
            .ToArray();
    }

    public string DescribeRelation(LocalizationEntry current, LocalizationEntry candidate)
    {
        var currentFamily = GetFamily(current);
        var candidateFamily = GetFamily(candidate);

        if (!string.IsNullOrEmpty(currentFamily) &&
            string.Equals(currentFamily, candidateFamily, StringComparison.OrdinalIgnoreCase))
        {
            return $"группа {currentFamily}";
        }

        if (!string.IsNullOrEmpty(current.Original) &&
            string.Equals(current.Original, candidate.Original, StringComparison.Ordinal))
        {
            return "тот же Original";
        }

        var currentSeries = GetSeriesFamily(currentFamily);
        var candidateSeries = GetSeriesFamily(candidateFamily);
        if (!string.IsNullOrEmpty(currentSeries) &&
            string.Equals(currentSeries, candidateSeries, StringComparison.OrdinalIgnoreCase))
        {
            return $"вариант {currentSeries}";
        }

        return "связанный ключ";
    }

    public static string GetKeyFamily(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        key = key.Trim();

        var numeric = NumericFamilyPattern.Match(key);
        if (numeric.Success)
            return numeric.Groups["family"].Value;

        var delimiterIndex = key.IndexOfAny(['-', ':', '/', '|']);
        if (delimiterIndex > 0)
            return key[..delimiterIndex];

        var dotParts = key.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (dotParts.Length >= 3)
            return string.Join('.', dotParts.Take(dotParts.Length - 1));

        return string.Empty;
    }

    public static string GetSeriesFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return string.Empty;

        var match = NumericSeriesPattern.Match(family);
        return match.Success
            ? match.Groups["series"].Value
            : string.Empty;
    }

    private void Build(IReadOnlyList<LocalizationEntry> entries)
    {
        string? previousFamily = null;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            _positions[entry] = i;

            if (!string.IsNullOrEmpty(entry.Original))
                Add(_byOriginal, entry.Original, entry);

            var family = GetKeyFamily(entry.Key);
            _families[entry] = family;

            if (!string.IsNullOrEmpty(family))
            {
                Add(_byFamily, family, entry);

                var series = GetSeriesFamily(family);
                if (!string.IsNullOrEmpty(series))
                    Add(_bySeries, series, entry);

                if (!string.Equals(previousFamily, family, StringComparison.OrdinalIgnoreCase))
                    _groupStarts.Add(entry);
            }

            previousFamily = family;
        }
    }

    private static void Add(
        Dictionary<string, List<LocalizationEntry>> lookup,
        string key,
        LocalizationEntry entry)
    {
        if (!lookup.TryGetValue(key, out var values))
        {
            values = [];
            lookup[key] = values;
        }

        values.Add(entry);
    }

    private static void AddRange(
        HashSet<LocalizationEntry> target,
        IEnumerable<LocalizationEntry> source,
        LocalizationEntry except)
    {
        foreach (var item in source)
        {
            if (!ReferenceEquals(item, except))
                target.Add(item);
        }
    }
}
