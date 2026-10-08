using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

/// <summary>
/// Immutable lookup index for relationships based on stable localization metadata.
/// The index avoids full-document scans when the selected row changes while keeping
/// memory overhead bounded for very large documents.
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

    // Most Originals occur once. Keep one direct reference for the common case and
    // allocate a List only when an Original is actually duplicated.
    private readonly Dictionary<string, LocalizationEntry> _firstByOriginal =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<LocalizationEntry>> _duplicateOriginals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<LocalizationEntry>> _byFamily =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LocalizationEntry>> _bySeries =
        new(StringComparer.OrdinalIgnoreCase);

    private EntryRelationIndex(LocalizationDocument document)
    {
        Document = document;
        Build(document.Entries);
    }

    public LocalizationDocument Document { get; }

    public static EntryRelationIndex For(LocalizationDocument document)
        => Cache.GetValue(document, static value => new EntryRelationIndex(value));

    public int GetPosition(LocalizationEntry entry)
        => Document.Entries.IndexOf(entry);

    public string GetFamily(LocalizationEntry entry)
        => GetKeyFamily(entry.Key);

    public bool StartsVisualGroup(LocalizationEntry entry)
    {
        var family = GetFamily(entry);
        if (string.IsNullOrEmpty(family))
            return false;

        var position = GetPosition(entry);
        if (position <= 0)
            return true;

        var previousFamily = GetFamily(Document.Entries[position - 1]);
        return !string.Equals(family, previousFamily, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<LocalizationEntry> GetSameOriginal(LocalizationEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Original))
            return [];

        if (_duplicateOriginals.TryGetValue(entry.Original, out var duplicates))
            return duplicates;

        return _firstByOriginal.TryGetValue(entry.Original, out var first)
            ? [first]
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
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (!string.IsNullOrEmpty(entry.Original))
                AddOriginal(entry.Original, entry);

            var family = GetKeyFamily(entry.Key);
            if (string.IsNullOrEmpty(family))
                continue;

            Add(_byFamily, family, entry);

            var series = GetSeriesFamily(family);
            if (!string.IsNullOrEmpty(series))
                Add(_bySeries, series, entry);
        }
    }

    private void AddOriginal(string original, LocalizationEntry entry)
    {
        if (!_firstByOriginal.TryGetValue(original, out var first))
        {
            _firstByOriginal[original] = entry;
            return;
        }

        if (!_duplicateOriginals.TryGetValue(original, out var duplicates))
        {
            duplicates = [first];
            _duplicateOriginals[original] = duplicates;
        }

        duplicates.Add(entry);
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
