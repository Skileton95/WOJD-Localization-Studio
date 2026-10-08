using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public readonly record struct EntryFilterCriteria(
    string Status,
    string? Namespace,
    string Search)
{
    public string NormalizedSearch => Search?.Trim() ?? string.Empty;

    public bool IsDefault
        => string.Equals(Status, "Все", StringComparison.Ordinal)
           && string.IsNullOrWhiteSpace(Namespace)
           && string.IsNullOrWhiteSpace(Search);
}

public static class EntryFilterService
{
    public static List<LocalizationEntry> Filter(
        IReadOnlyList<LocalizationEntry> entries,
        EntryFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        if (criteria.IsDefault)
            return entries as List<LocalizationEntry> ?? entries.ToList();

        var result = new List<LocalizationEntry>(Math.Min(entries.Count, 4096));
        var query = criteria.NormalizedSearch;

        for (var i = 0; i < entries.Count; i++)
        {
            if ((i & 0x7FF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[i];
            if (Matches(entry, criteria, query))
                result.Add(entry);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static bool Matches(
        LocalizationEntry entry,
        EntryFilterCriteria criteria)
        => Matches(entry, criteria, criteria.NormalizedSearch);

    public static bool CanNarrow(
        EntryFilterCriteria previous,
        EntryFilterCriteria next)
    {
        if (!string.Equals(previous.Status, next.Status, StringComparison.Ordinal)
            || !string.Equals(previous.Namespace, next.Namespace, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var previousQuery = previous.NormalizedSearch;
        var nextQuery = next.NormalizedSearch;
        return previousQuery.Length > 0
               && nextQuery.Length >= previousQuery.Length
               && nextQuery.StartsWith(previousQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(
        LocalizationEntry entry,
        EntryFilterCriteria criteria,
        string query)
    {
        if (string.Equals(criteria.Status, "Ошибки", StringComparison.Ordinal))
        {
            if (!entry.HasValidationIssues)
                return false;
        }
        else if (!string.Equals(criteria.Status, "Все", StringComparison.Ordinal)
                 && !string.Equals(entry.StatusText, criteria.Status, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(criteria.Namespace)
            && !string.Equals(entry.Namespace, criteria.Namespace, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.Length == 0)
            return true;

        return entry.Namespace.Contains(query, StringComparison.OrdinalIgnoreCase)
               || entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
               || entry.Original.Contains(query, StringComparison.OrdinalIgnoreCase)
               || entry.Translation.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
