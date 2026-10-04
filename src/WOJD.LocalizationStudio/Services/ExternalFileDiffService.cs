using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record ExternalTranslationDiff(
    int Index,
    string Namespace,
    string Key,
    string CurrentTranslation,
    string DiskTranslation);

public sealed record ExternalFileDiffResult(
    LocalizationDocument DiskDocument,
    IReadOnlyList<ExternalTranslationDiff> Changes,
    int MissingRows,
    int AddedRows);

public static class ExternalFileDiffService
{
    public static async Task<ExternalFileDiffResult> CompareAsync(
        LocalizationDocument current,
        CancellationToken cancellationToken = default)
    {
        var adapter = new NdjsonLocalizationAdapter();
        var disk = await adapter.LoadAsync(current.FilePath, cancellationToken);

        var diskByIdentity = disk.Entries.ToDictionary(
            x => Identity(x),
            StringComparer.Ordinal);
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        var changes = new List<ExternalTranslationDiff>();
        var missing = 0;

        foreach (var entry in current.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Identity(entry);
            currentKeys.Add(id);

            if (!diskByIdentity.TryGetValue(id, out var diskEntry))
            {
                missing++;
                continue;
            }

            if (!string.Equals(
                    entry.Translation,
                    diskEntry.Translation,
                    StringComparison.Ordinal))
            {
                changes.Add(new ExternalTranslationDiff(
                    entry.Index,
                    entry.Namespace,
                    entry.Key,
                    entry.Translation,
                    diskEntry.Translation));
            }
        }

        var added = diskByIdentity.Keys.Count(x => !currentKeys.Contains(x));
        return new ExternalFileDiffResult(disk, changes, missing, added);
    }

    public static int ApplyDiskTranslations(
        LocalizationDocument current,
        LocalizationDocument disk)
    {
        var diskByIdentity = disk.Entries.ToDictionary(
            x => Identity(x),
            StringComparer.Ordinal);
        var changed = 0;

        using (EntryHistoryService.BeginOperation("Перезагрузка внешней версии"))
        {
            foreach (var entry in current.Entries)
            {
                if (!diskByIdentity.TryGetValue(Identity(entry), out var diskEntry))
                    continue;

                if (string.Equals(entry.Translation, diskEntry.Translation, StringComparison.Ordinal))
                    continue;

                entry.Translation = diskEntry.Translation;
                changed++;
            }
        }

        return changed;
    }

    private static string Identity(LocalizationEntry entry)
        => string.Concat(
            entry.Index.ToString(),
            "\u001F",
            entry.Namespace,
            "\u001F",
            entry.Key);
}
