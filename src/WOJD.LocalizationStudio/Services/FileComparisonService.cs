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

    public bool HasExistingCurrentTranslation
        => CurrentEntry is not null &&
           !string.IsNullOrWhiteSpace(CurrentEntry.Translation);

    public bool HasOldTranslation
        => OldEntry is not null &&
           !string.IsNullOrWhiteSpace(OldEntry.Translation);

    // Безопасный перенос не перезаписывает уже существующий перевод.
    public bool CanTransfer
        => CurrentEntry is not null &&
           OldEntry is not null &&
           !SourceChanged &&
           !HasExistingCurrentTranslation &&
           HasOldTranslation;
}

public sealed record FileComparisonResult(
    List<FileComparisonItem> Items,
    int Added,
    int Removed,
    int OriginalChanged,
    int TranslationChanged,
    int Transferable,
    int NeedsReview);

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
        var transferable = 0;
        var needsReview = 0;

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
                            "Новая строка",
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
                    needsReview++;
                    items.Add(
                        CreateItem(
                            "Нужно проверить вручную",
                            currentEntry,
                            oldEntry));
                    continue;
                }

                if (string.Equals(
                        currentEntry.Translation,
                        oldEntry.Translation,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                translationChanged++;

                if (string.IsNullOrWhiteSpace(currentEntry.Translation) &&
                    !string.IsNullOrWhiteSpace(oldEntry.Translation))
                {
                    transferable++;
                    items.Add(
                        CreateItem(
                            "Перевод можно перенести",
                            currentEntry,
                            oldEntry));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentEntry.Translation) &&
                    !string.IsNullOrWhiteSpace(oldEntry.Translation))
                {
                    needsReview++;
                    items.Add(
                        CreateItem(
                            "Нужно проверить вручную",
                            currentEntry,
                            oldEntry));
                    continue;
                }

                items.Add(
                    CreateItem(
                        "Изменён перевод",
                        currentEntry,
                        oldEntry));
            }
        }

        return new FileComparisonResult(
            items,
            added,
            removed,
            originalChanged,
            translationChanged,
            transferable,
            needsReview);
    }

    public static (
        int Transferred,
        int SkippedChangedSource,
        int SkippedExistingTranslation)
        TransferTranslations(
            IEnumerable<FileComparisonItem> items)
    {
        var transferred = 0;
        var skippedChangedSource = 0;
        var skippedExistingTranslation = 0;

        foreach (var item in items)
        {
            if (item.SourceChanged && item.HasOldTranslation)
            {
                skippedChangedSource++;
                continue;
            }

            if (!item.SourceChanged &&
                item.HasOldTranslation &&
                item.HasExistingCurrentTranslation &&
                item.CurrentEntry is not null &&
                item.OldEntry is not null &&
                !string.Equals(
                    item.CurrentEntry.Translation,
                    item.OldEntry.Translation,
                    StringComparison.Ordinal))
            {
                skippedExistingTranslation++;
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

        return (
            transferred,
            skippedChangedSource,
            skippedExistingTranslation);
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
                $"{entry.Namespace}\u001F{entry.Key}";

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
