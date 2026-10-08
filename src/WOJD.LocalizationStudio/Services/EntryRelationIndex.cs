using System.Runtime.CompilerServices;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

/// <summary>
/// Lookup index for relationships based on stable localization metadata.
/// LocalizationDocument initializes the index before its entries are loaded, so the
/// normal NDJSON background parser extends it incrementally instead of causing a
/// full-document scan on the UI thread when the first row is selected.
/// </summary>
public sealed class EntryRelationIndex
{
    private static readonly ConditionalWeakTable<LocalizationDocument, EntryRelationIndex> Cache = new();

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

    internal static void Initialize(LocalizationDocument document)
        => _ = Cache.GetValue(document, static value => new EntryRelationIndex(value));

    internal static void AppendLoadedEntry(LocalizationDocument document, LocalizationEntry entry)
    {
        // During normal file loading Initialize() has already created the empty
        // index. If a caller structurally changed a document and invalidated it,
        // leave it invalid and let For() rebuild from the complete list later.
        if (Cache.TryGetValue(document, out var index))
            index.AddEntry(entry);
    }

    internal static void Invalidate(LocalizationDocument document)
        => Cache.Remove(document);

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

        // Every index bucket is populated in source-file order. Merge the few sorted
        // buckets and stop as soon as maxCount unique rows are found. The old code
        // materialized and sorted the entire union, which made a click on a very
        // common Original (thousands of rows) unexpectedly expensive.
        var sources = new List<IReadOnlyList<LocalizationEntry>>(3);

        var sameFamily = GetSameFamily(entry);
        if (sameFamily.Count > 0)
            sources.Add(sameFamily);

        var sameOriginal = GetSameOriginal(entry);
        if (sameOriginal.Count > 0)
            sources.Add(sameOriginal);

        var family = GetFamily(entry);
        var series = GetSeriesFamily(family);
        if (!string.IsNullOrEmpty(series) &&
            _bySeries.TryGetValue(series, out var seriesEntries) &&
            seriesEntries.Count > 0)
        {
            sources.Add(seriesEntries);
        }

        if (sources.Count == 0)
            return [];

        var cursors = new int[sources.Count];
        var seen = new HashSet<LocalizationEntry>();
        var result = new List<LocalizationEntry>(Math.Min(maxCount, 32));

        while (result.Count < maxCount)
        {
            LocalizationEntry? best = null;
            var bestSource = -1;
            var bestPosition = int.MaxValue;

            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var source = sources[sourceIndex];
                var cursor = cursors[sourceIndex];

                while (cursor < source.Count &&
                       (ReferenceEquals(source[cursor], entry) || seen.Contains(source[cursor])))
                {
                    cursor++;
                }

                cursors[sourceIndex] = cursor;
                if (cursor >= source.Count)
                    continue;

                var candidate = source[cursor];
                var position = GetPosition(candidate);
                if (position >= 0 && position < bestPosition)
                {
                    best = candidate;
                    bestSource = sourceIndex;
                    bestPosition = position;
                }
            }

            if (best is null || bestSource < 0)
                break;

            cursors[bestSource]++;
            if (seen.Add(best))
                result.Add(best);
        }

        return result;
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

        // Common WOJD keys use a stable prefix followed by a semantic suffix,
        // e.g. 450_0-SkillName / 450_0-SkillDesc. Parse this without Regex because
        // the method runs for every row in very large files.
        var delimiterIndex = key.IndexOfAny(['-', ':', '/', '|']);
        if (delimiterIndex > 0)
            return key[..delimiterIndex];

        var firstDot = key.IndexOf('.');
        if (firstDot <= 0)
            return string.Empty;

        var firstPart = key[..firstDot];
        if (LooksLikeNumericFamily(firstPart))
            return firstPart;

        // UI.Common.Confirm -> UI.Common
        var lastDot = key.LastIndexOf('.');
        return lastDot > firstDot
            ? key[..lastDot]
            : string.Empty;
    }

    public static string GetSeriesFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return string.Empty;

        var separator = family.LastIndexOf('_');
        if (separator <= 0 || separator == family.Length - 1)
            return string.Empty;

        for (var i = separator + 1; i < family.Length; i++)
        {
            if (!char.IsDigit(family[i]))
                return string.Empty;
        }

        return family[..separator];
    }

    private static bool LooksLikeNumericFamily(string value)
    {
        var separator = value.IndexOf('_');
        if (separator <= 0 || separator == value.Length - 1)
            return false;

        var sawDigitBeforeSeparator = false;
        for (var i = 0; i < separator; i++)
        {
            var ch = value[i];
            if (char.IsDigit(ch))
            {
                sawDigitBeforeSeparator = true;
                continue;
            }

            if (!char.IsLetter(ch))
                return false;
        }

        if (!sawDigitBeforeSeparator)
            return false;

        var needDigit = true;
        for (var i = separator + 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '_')
            {
                if (needDigit)
                    return false;
                needDigit = true;
                continue;
            }

            if (!char.IsDigit(ch))
                return false;

            needDigit = false;
        }

        return !needDigit;
    }

    private void Build(IReadOnlyList<LocalizationEntry> entries)
    {
        for (var i = 0; i < entries.Count; i++)
            AddEntry(entries[i]);
    }

    private void AddEntry(LocalizationEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Original))
            AddOriginal(entry.Original, entry);

        var family = GetKeyFamily(entry.Key);
        if (string.IsNullOrEmpty(family))
            return;

        Add(_byFamily, family, entry);

        var series = GetSeriesFamily(family);
        if (!string.IsNullOrEmpty(series))
            Add(_bySeries, series, entry);
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
}
