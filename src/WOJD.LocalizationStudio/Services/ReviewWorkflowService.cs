using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public enum ReviewState
{
    Unreviewed,
    Reviewed,
    NeedsFix,
    Skipped
}

public sealed record ReviewCounts(
    int Unreviewed,
    int Reviewed,
    int NeedsFix,
    int Skipped);

public static class ReviewWorkflowService
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ReviewStore> Stores =
        new(StringComparer.OrdinalIgnoreCase);

    private static string RootFolder
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "ReviewStatus");

    public static void LoadDocument(
        string filePath,
        IEnumerable<LocalizationEntry> entries)
    {
        lock (Sync)
        {
            var store = GetOrLoadStore(filePath);
            var changed = false;

            foreach (var entry in entries)
            {
                var id = GetEntryId(entry);

                if (!store.Entries.TryGetValue(id, out var record) ||
                    record.State != ReviewState.Reviewed)
                {
                    continue;
                }

                if (!string.Equals(
                        record.TranslationHash,
                        HashTranslation(entry.Translation),
                        StringComparison.Ordinal))
                {
                    store.Entries.Remove(id);
                    changed = true;
                }
            }

            if (changed)
                SaveStore(store);
        }
    }

    public static ReviewState GetStatus(
        string filePath,
        LocalizationEntry entry)
    {
        lock (Sync)
        {
            var store = GetOrLoadStore(filePath);
            var id = GetEntryId(entry);

            if (!store.Entries.TryGetValue(id, out var record))
                return ReviewState.Unreviewed;

            if (record.State == ReviewState.Reviewed &&
                !string.Equals(
                    record.TranslationHash,
                    HashTranslation(entry.Translation),
                    StringComparison.Ordinal))
            {
                store.Entries.Remove(id);
                store.Dirty = true;
                return ReviewState.Unreviewed;
            }

            return record.State;
        }
    }

    public static void SetStatus(
        string filePath,
        LocalizationEntry entry,
        ReviewState state)
        => SetStatus(filePath, [entry], state);

    public static void SetStatus(
        string filePath,
        IEnumerable<LocalizationEntry> entries,
        ReviewState state)
    {
        lock (Sync)
        {
            var store = GetOrLoadStore(filePath);

            foreach (var entry in entries.Distinct())
            {
                var id = GetEntryId(entry);

                if (state == ReviewState.Unreviewed)
                {
                    store.Entries.Remove(id);
                    continue;
                }

                store.Entries[id] =
                    new ReviewRecord
                    {
                        Index = entry.Index,
                        Namespace = entry.Namespace,
                        Key = entry.Key,
                        State = state,
                        TranslationHash = state == ReviewState.Reviewed
                            ? HashTranslation(entry.Translation)
                            : string.Empty,
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
            }

            SaveStore(store);
        }
    }

    public static bool OnTranslationChanged(
        string filePath,
        LocalizationEntry entry)
    {
        lock (Sync)
        {
            var store = GetOrLoadStore(filePath);
            var id = GetEntryId(entry);

            if (!store.Entries.TryGetValue(id, out var record) ||
                record.State != ReviewState.Reviewed)
            {
                return false;
            }

            store.Entries.Remove(id);
            SaveStore(store);
            return true;
        }
    }

    public static ReviewCounts GetCounts(
        string filePath,
        IReadOnlyCollection<LocalizationEntry> entries)
    {
        lock (Sync)
        {
            var store = GetOrLoadStore(filePath);
            var reviewed = 0;
            var needsFix = 0;
            var skipped = 0;
            var staleReviewed = new List<string>();

            foreach (var entry in entries)
            {
                var id = GetEntryId(entry);

                if (!store.Entries.TryGetValue(id, out var record))
                    continue;

                if (record.State == ReviewState.Reviewed &&
                    !string.Equals(
                        record.TranslationHash,
                        HashTranslation(entry.Translation),
                        StringComparison.Ordinal))
                {
                    staleReviewed.Add(id);
                    continue;
                }

                switch (record.State)
                {
                    case ReviewState.Reviewed:
                        reviewed++;
                        break;
                    case ReviewState.NeedsFix:
                        needsFix++;
                        break;
                    case ReviewState.Skipped:
                        skipped++;
                        break;
                }
            }

            if (staleReviewed.Count > 0)
            {
                foreach (var id in staleReviewed)
                    store.Entries.Remove(id);

                store.Dirty = true;
            }

            if (store.Dirty)
                SaveStore(store);

            var unreviewed =
                Math.Max(0, entries.Count - reviewed - needsFix - skipped);

            return new ReviewCounts(
                unreviewed,
                reviewed,
                needsFix,
                skipped);
        }
    }

    public static string ToDisplayText(ReviewState state)
        => state switch
        {
            ReviewState.Reviewed => "Проверено",
            ReviewState.NeedsFix => "Требует правки",
            ReviewState.Skipped => "Пропустить",
            _ => "Не проверено"
        };

    private static ReviewStore GetOrLoadStore(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        if (Stores.TryGetValue(fullPath, out var cached))
            return cached;

        Directory.CreateDirectory(RootFolder);
        var storagePath = GetStoragePath(fullPath);
        ReviewStore store;

        try
        {
            if (File.Exists(storagePath))
            {
                store =
                    JsonSerializer.Deserialize<ReviewStore>(
                        File.ReadAllText(storagePath, Encoding.UTF8),
                        JsonOptions)
                    ?? new ReviewStore();
            }
            else
            {
                store = new ReviewStore();
            }
        }
        catch
        {
            store = new ReviewStore();
        }

        store.FilePath = fullPath;
        store.Dirty = false;
        Stores[fullPath] = store;
        return store;
    }

    private static void SaveStore(ReviewStore store)
    {
        Directory.CreateDirectory(RootFolder);
        var path = GetStoragePath(store.FilePath);
        var temp = path + ".tmp";

        var json =
            JsonSerializer.Serialize(
                store,
                JsonOptions);

        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, path, true);
        store.Dirty = false;
    }

    private static string GetStoragePath(string fullPath)
        => Path.Combine(
            RootFolder,
            HashPath(fullPath) + ".review.json");

    private static string GetEntryId(LocalizationEntry entry)
        => string.Join(
            "\u001F",
            entry.Index.ToString(),
            entry.Namespace,
            entry.Key);

    private static string HashPath(string path)
        => Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        Path.GetFullPath(path).ToUpperInvariant())))
            .ToLowerInvariant();

    private static string HashTranslation(string translation)
        => Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(translation ?? string.Empty)))
            .ToLowerInvariant();

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private sealed class ReviewStore
    {
        public string FilePath { get; set; } = string.Empty;
        public Dictionary<string, ReviewRecord> Entries { get; set; } =
            new(StringComparer.Ordinal);

        [System.Text.Json.Serialization.JsonIgnore]
        public bool Dirty { get; set; }
    }

    private sealed class ReviewRecord
    {
        public int Index { get; set; }
        public string Namespace { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public ReviewState State { get; set; }
        public string TranslationHash { get; set; } = string.Empty;
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
