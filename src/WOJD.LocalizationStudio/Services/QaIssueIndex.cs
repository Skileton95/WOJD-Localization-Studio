using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class QaIssueIndex
{
    public static readonly string[] CategoryIds =
    [
        "all", "untranslated", "sourceMissing", "tags", "placeholders",
        "newlines", "glossary", "consistency", "sameSource", "suspicious"
    ];

    private readonly Dictionary<string, List<LocalizationEntry>> _lists =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<LocalizationEntry>> _sets =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _inconsistentOriginals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _consistencyVariants =
        new(StringComparer.Ordinal);

    public QaIssueIndex(IReadOnlyList<LocalizationEntry>? entries)
    {
        foreach (var id in CategoryIds)
        {
            _lists[id] = [];
            _sets[id] = [];
        }

        Build(entries);
    }

    public int LastIncrementalExaminedEntries { get; private set; }

    public int Count(string id)
        => _sets.TryGetValue(id, out var set) ? set.Count : 0;

    public IReadOnlyList<LocalizationEntry> GetCategory(string id)
        => _lists.TryGetValue(id, out var list) ? list : [];

    public bool Contains(string id, LocalizationEntry entry)
        => _sets.TryGetValue(id, out var set) && set.Contains(entry);

    public IReadOnlyList<string> GetConsistencyVariants(string? original)
    {
        if (string.IsNullOrWhiteSpace(original))
            return [];

        return _consistencyVariants.TryGetValue(original, out var variants)
            ? variants
            : [];
    }

    public IReadOnlyList<LocalizationEntry> GetFamilyCategory(
        string id,
        LocalizationEntry anchor,
        EntryRelationIndex relations)
    {
        if (!_sets.TryGetValue(id, out var membership))
            return [];

        var familyRows = relations.GetSameFamily(anchor);
        if (familyRows.Count == 0)
            return [];

        var result = new List<LocalizationEntry>(familyRows.Count);
        foreach (var entry in familyRows)
        {
            if (membership.Contains(entry))
                result.Add(entry);
        }

        return result;
    }

    public void UpdateAfterTranslation(
        LocalizationEntry changed,
        EntryRelationIndex relations)
    {
        IReadOnlyList<LocalizationEntry> affected;
        if (string.IsNullOrWhiteSpace(changed.Original))
        {
            affected = [changed];
        }
        else
        {
            var sameOriginal = relations.GetSameOriginal(changed);
            affected = sameOriginal.Count > 0 ? sameOriginal : [changed];
            RebuildConsistencyForOriginal(changed.Original, affected);
        }

        LastIncrementalExaminedEntries = affected.Count;

        foreach (var entry in affected)
            UpdateMembership(entry, relations);
    }

    private void Build(IReadOnlyList<LocalizationEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
            return;

        var firstTranslationByOriginal = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Original) || string.IsNullOrWhiteSpace(entry.Translation))
                continue;

            if (!firstTranslationByOriginal.TryGetValue(entry.Original, out var first))
            {
                firstTranslationByOriginal[entry.Original] = entry.Translation;
                continue;
            }

            if (!string.Equals(first, entry.Translation, StringComparison.Ordinal))
                _inconsistentOriginals.Add(entry.Original);
        }

        foreach (var entry in entries)
        {
            foreach (var id in CategoryIds)
            {
                if (!Matches(id, entry))
                    continue;

                _sets[id].Add(entry);
                _lists[id].Add(entry);
            }

            if (IsConsistencyProblem(entry))
                CacheConsistencyVariant(entry);
        }
    }

    private void RebuildConsistencyForOriginal(
        string original,
        IReadOnlyList<LocalizationEntry> entries)
    {
        var variants = new List<string>(6);
        string? first = null;
        var inconsistent = false;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Translation))
                continue;

            first ??= entry.Translation;
            if (!string.Equals(first, entry.Translation, StringComparison.Ordinal))
                inconsistent = true;

            if (variants.Count < 6 && !variants.Contains(entry.Translation, StringComparer.Ordinal))
                variants.Add(entry.Translation);
        }

        if (inconsistent)
            _inconsistentOriginals.Add(original);
        else
            _inconsistentOriginals.Remove(original);

        if (variants.Count > 0)
            _consistencyVariants[original] = variants;
        else
            _consistencyVariants.Remove(original);
    }

    private void UpdateMembership(
        LocalizationEntry entry,
        EntryRelationIndex relations)
    {
        foreach (var id in CategoryIds)
        {
            var shouldContain = Matches(id, entry);
            var set = _sets[id];
            var list = _lists[id];

            if (shouldContain)
            {
                if (set.Add(entry))
                    InsertInSourceOrder(list, entry, relations);
            }
            else if (set.Remove(entry))
            {
                list.Remove(entry);
            }
        }

        if (IsConsistencyProblem(entry))
            CacheConsistencyVariant(entry);
    }

    private static void InsertInSourceOrder(
        List<LocalizationEntry> list,
        LocalizationEntry entry,
        EntryRelationIndex relations)
    {
        var position = relations.GetPosition(entry);
        var low = 0;
        var high = list.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (relations.GetPosition(list[mid]) <= position)
                low = mid + 1;
            else
                high = mid;
        }

        list.Insert(low, entry);
    }

    private bool Matches(string id, LocalizationEntry entry)
        => id switch
        {
            "untranslated" => string.IsNullOrWhiteSpace(entry.Translation),
            "sourceMissing" => entry.HasSourceMissingIssue || string.IsNullOrWhiteSpace(entry.Original),
            "tags" => entry.HasTagIssues,
            "placeholders" => entry.HasPlaceholderIssues,
            "newlines" => entry.HasNewLineIssues,
            "glossary" => entry.HasGlossaryIssue,
            "consistency" => IsConsistencyProblem(entry),
            "sameSource" => entry.HasSameAsSourceIssue,
            "suspicious" => entry.HasSuspiciousLengthIssue || entry.HasProfileRuleIssue,
            _ => string.IsNullOrWhiteSpace(entry.Translation)
                 || entry.HasValidationIssues
                 || IsConsistencyProblem(entry)
        };

    private bool IsConsistencyProblem(LocalizationEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Original)
           && _inconsistentOriginals.Contains(entry.Original);

    private void CacheConsistencyVariant(LocalizationEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Original) || string.IsNullOrWhiteSpace(entry.Translation))
            return;

        if (!_consistencyVariants.TryGetValue(entry.Original, out var variants))
        {
            variants = [];
            _consistencyVariants[entry.Original] = variants;
        }

        if (variants.Count >= 6 || variants.Contains(entry.Translation, StringComparer.Ordinal))
            return;

        variants.Add(entry.Translation);
    }
}
