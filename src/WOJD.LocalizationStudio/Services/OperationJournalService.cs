using System.IO;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record OperationJournalItem(
    DateTimeOffset Timestamp,
    string Reason,
    int EntryIndex,
    string Namespace,
    string Key,
    string Before,
    string After);

public static class OperationJournalService
{
    private const int MaxItems = 5000;
    private static readonly object Sync = new();
    private static readonly List<OperationJournalItem> Items = [];
    private static bool _loaded;

    public static event EventHandler? Changed;

    public static string JournalPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "operation-journal.json");

    public static void Record(
        LocalizationEntry entry,
        string before,
        string after,
        string reason)
    {
        if (entry is null || string.Equals(before, after, StringComparison.Ordinal))
            return;

        lock (Sync)
        {
            EnsureLoaded();
            Items.Add(new OperationJournalItem(
                DateTimeOffset.Now,
                string.IsNullOrWhiteSpace(reason) ? "Изменение перевода" : reason,
                entry.Index,
                entry.Namespace,
                entry.Key,
                before,
                after));

            if (Items.Count > MaxItems)
                Items.RemoveRange(0, Items.Count - MaxItems);

            SaveCore();
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static IReadOnlyList<OperationJournalItem> GetItems(int limit = 1000)
    {
        lock (Sync)
        {
            EnsureLoaded();
            return Items
                .OrderByDescending(x => x.Timestamp)
                .Take(Math.Clamp(limit, 1, MaxItems))
                .ToList();
        }
    }

    public static IReadOnlyList<(string Reason, DateTimeOffset From, DateTimeOffset To, int Changes)> GetOperationGroups(int limit = 200)
    {
        lock (Sync)
        {
            EnsureLoaded();

            var groups = new List<(string Reason, DateTimeOffset From, DateTimeOffset To, int Changes)>();
            string? currentReason = null;
            DateTimeOffset from = default;
            DateTimeOffset to = default;
            var count = 0;

            foreach (var item in Items.OrderBy(x => x.Timestamp))
            {
                var startsNew =
                    currentReason is null ||
                    !string.Equals(currentReason, item.Reason, StringComparison.Ordinal) ||
                    item.Timestamp - to > TimeSpan.FromSeconds(3);

                if (startsNew && currentReason is not null)
                    groups.Add((currentReason, from, to, count));

                if (startsNew)
                {
                    currentReason = item.Reason;
                    from = item.Timestamp;
                    count = 0;
                }

                to = item.Timestamp;
                count++;
            }

            if (currentReason is not null)
                groups.Add((currentReason, from, to, count));

            return groups
                .OrderByDescending(x => x.To)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToList();
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            EnsureLoaded();
            Items.Clear();
            SaveCore();
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;

        try
        {
            if (!File.Exists(JournalPath))
                return;

            var loaded = JsonSerializer.Deserialize<List<OperationJournalItem>>(
                File.ReadAllText(JournalPath, Encoding.UTF8),
                JsonOptions);

            if (loaded is not null)
                Items.AddRange(loaded.TakeLast(MaxItems));
        }
        catch
        {
            // Повреждённый журнал не должен мешать запуску редактора.
        }
    }

    private static void SaveCore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        var temp = JournalPath + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(Items, JsonOptions),
            new UTF8Encoding(false));
        File.Move(temp, JournalPath, true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
