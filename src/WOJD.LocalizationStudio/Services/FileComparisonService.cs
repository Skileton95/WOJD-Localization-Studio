using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class FileComparisonItem
{
    public required string Namespace { get; init; }
    public required string Key { get; init; }
    public required string ChangeType { get; init; }

    public string OldOriginal { get; init; } = string.Empty;
    public string NewOriginal { get; init; } = string.Empty;
    public string OldTranslation { get; init; } = string.Empty;
    public string NewTranslation
        => CurrentEntry?.Translation ?? string.Empty;

    public LocalizationEntry? CurrentEntry { get; init; }
    public LocalizationEntry? OldEntry { get; init; }

    public bool SourceChanged
        => CurrentEntry is not null &&
           OldEntry is not null &&
           !string.Equals(
               CurrentEntry.Original,
               OldEntry.Original,
               StringComparison.Ordinal);

    public bool IsReadOnly { get; internal set; }
    public bool CanTransfer
        => !IsReadOnly && CurrentEntry is not null &&
           OldEntry is not null &&
           !SourceChanged &&
           !string.IsNullOrWhiteSpace(OldEntry.Translation) &&
           !string.Equals(
               CurrentEntry.Translation,
               OldEntry.Translation,
               StringComparison.Ordinal);
}

public sealed record FileComparisonResult(
    List<FileComparisonItem> Items,
    int Added,
    int Removed,
    int OriginalChanged,
    int TranslationChanged);

public static class FileComparisonService
{
    public static FileComparisonResult Compare(
        LocalizationDocument current,
        LocalizationDocument oldDocument)
    {
        var items = new List<FileComparisonItem>();
        var added = 0;
        var removed = 0;
        var originalChanged = 0;
        var translationChanged = 0;

        var currentGroups =
            GroupByIdentity(current.Entries);

        var oldGroups =
            GroupByIdentity(oldDocument.Entries);

        var keys =
            currentGroups.Keys
                .Concat(oldGroups.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

        foreach (var identity in keys)
        {
            currentGroups.TryGetValue(
                identity,
                out var currentItems);

            oldGroups.TryGetValue(
                identity,
                out var oldItems);

            currentItems ??= [];
            oldItems ??= [];

            if (currentItems.Count > 1 || oldItems.Count > 1)
            {
                items.AddRange(currentItems.Select(e => CreateItem("Коллизия — перенос заблокирован", e, null)));
                items.AddRange(oldItems.Select(e => CreateItem("Коллизия — перенос заблокирован", null, e)));
                continue;
            }
            var count =
                Math.Max(
                    currentItems.Count,
                    oldItems.Count);

            for (var i = 0; i < count; i++)
            {
                var currentEntry =
                    i < currentItems.Count
                        ? currentItems[i]
                        : null;

                var oldEntry =
                    i < oldItems.Count
                        ? oldItems[i]
                        : null;

                if (currentEntry is not null &&
                    oldEntry is null)
                {
                    added++;
                    items.Add(
                        CreateItem(
                            "Добавлено",
                            currentEntry,
                            null));
                    continue;
                }

                if (currentEntry is null &&
                    oldEntry is not null)
                {
                    removed++;
                    items.Add(
                        CreateItem(
                            "Удалено",
                            null,
                            oldEntry));
                    continue;
                }

                if (currentEntry is null ||
                    oldEntry is null)
                {
                    continue;
                }

                if (!string.Equals(
                        currentEntry.Original,
                        oldEntry.Original,
                        StringComparison.Ordinal))
                {
                    originalChanged++;
                    items.Add(
                        CreateItem(
                            "Изменён оригинал",
                            currentEntry,
                            oldEntry));
                    continue;
                }

                if (!string.Equals(
                        currentEntry.Translation,
                        oldEntry.Translation,
                        StringComparison.Ordinal))
                {
                    translationChanged++;
                    items.Add(
                        CreateItem(
                            "Изменён перевод",
                            currentEntry,
                            oldEntry));
                }
            }
        }

        foreach (var item in items) item.IsReadOnly = current.IsReadOnly;
        return new FileComparisonResult(
            items,
            added,
            removed,
            originalChanged,
            translationChanged);
    }

    public static (int Transferred, int SkippedChangedSource)
        TransferTranslations(
            IEnumerable<FileComparisonItem> items)
    {
        var transferred = 0;
        var skipped = 0;

        foreach (var item in items)
        {
            if (item.SourceChanged &&
                item.OldEntry is not null &&
                !string.IsNullOrWhiteSpace(
                    item.OldEntry.Translation))
            {
                skipped++;
                continue;
            }

            if (!item.CanTransfer ||
                item.CurrentEntry is null ||
                item.OldEntry is null)
            {
                continue;
            }

            item.CurrentEntry.Translation =
                item.OldEntry.Translation;

            transferred++;
        }

        return (transferred, skipped);
    }

    private static Dictionary<string, List<LocalizationEntry>>
        GroupByIdentity(
            IEnumerable<LocalizationEntry> entries)
    {
        var result =
            new Dictionary<string, List<LocalizationEntry>>(
                StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var key =
                System.Text.Json.JsonSerializer.Serialize(new[] { entry.Namespace, entry.Key });

            if (!result.TryGetValue(
                    key,
                    out var list))
            {
                list = [];
                result[key] = list;
            }

            list.Add(entry);
        }

        return result;
    }

    private static FileComparisonItem CreateItem(
        string changeType,
        LocalizationEntry? current,
        LocalizationEntry? oldEntry)
    {
        var source =
            current ?? oldEntry!;

        return new FileComparisonItem
        {
            Namespace = source.Namespace,
            Key = source.Key,
            ChangeType = changeType,
            OldOriginal =
                oldEntry?.Original
                ?? string.Empty,
            NewOriginal =
                current?.Original
                ?? string.Empty,
            OldTranslation =
                oldEntry?.Translation
                ?? string.Empty,
            CurrentEntry = current,
            OldEntry = oldEntry
        };
    }
}
