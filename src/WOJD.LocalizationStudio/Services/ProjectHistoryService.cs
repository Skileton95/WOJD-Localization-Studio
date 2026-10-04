using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record ProjectHistoryItem(
    DateTimeOffset Time,
    string Operation,
    int AffectedEntries,
    string Details);

public static class ProjectHistoryService
{
    private const int MaxItemsPerFile = 1000;
    private static readonly object Sync = new();
    private static readonly ConditionalWeakTable<LocalizationEntry, FileHolder> EntryFiles = new();

    private static string RootFolder
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "ProjectHistory");

    public static void RegisterDocument(LocalizationDocument document)
    {
        var path = Path.GetFullPath(document.FilePath);

        lock (Sync)
        {
            foreach (var entry in document.Entries)
            {
                EntryFiles.Remove(entry);
                EntryFiles.Add(entry, new FileHolder(path));
            }
        }
    }

    public static void UnregisterDocument(LocalizationDocument document)
    {
        lock (Sync)
        {
            foreach (var entry in document.Entries)
                EntryFiles.Remove(entry);
        }
    }

    public static void RecordEntry(
        LocalizationEntry entry,
        string operation,
        string before,
        string after)
    {
        string? filePath = null;

        lock (Sync)
        {
            if (EntryFiles.TryGetValue(entry, out var holder))
                filePath = holder.FilePath;
        }

        if (string.IsNullOrWhiteSpace(filePath) ||
            string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        var details =
            $"#{entry.Index} {entry.Namespace}:{entry.Key} | " +
            $"{Shorten(before)} → {Shorten(after)}";

        Record(filePath, operation, 1, details);
    }

    public static void Record(
        string filePath,
        string operation,
        int affectedEntries,
        string details = "")
    {
        if (string.IsNullOrWhiteSpace(filePath) ||
            string.IsNullOrWhiteSpace(operation))
        {
            return;
        }

        lock (Sync)
        {
            var items = LoadCore(filePath).ToList();
            items.Insert(
                0,
                new ProjectHistoryItem(
                    DateTimeOffset.Now,
                    operation.Trim(),
                    Math.Max(0, affectedEntries),
                    details?.Trim() ?? string.Empty));

            if (items.Count > MaxItemsPerFile)
                items.RemoveRange(MaxItemsPerFile, items.Count - MaxItemsPerFile);

            SaveCore(filePath, items);
        }
    }

    public static IReadOnlyList<ProjectHistoryItem> GetHistory(string filePath)
    {
        lock (Sync)
            return LoadCore(filePath);
    }

    private static IReadOnlyList<ProjectHistoryItem> LoadCore(string filePath)
    {
        try
        {
            var path = GetStoragePath(filePath);
            if (!File.Exists(path))
                return [];

            return JsonSerializer.Deserialize<List<ProjectHistoryItem>>(
                       File.ReadAllText(path, Encoding.UTF8),
                       JsonOptions)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveCore(
        string filePath,
        IReadOnlyList<ProjectHistoryItem> items)
    {
        Directory.CreateDirectory(RootFolder);
        var path = GetStoragePath(filePath);
        var temp = path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(items, JsonOptions),
            new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private static string GetStoragePath(string filePath)
    {
        var full = Path.GetFullPath(filePath).ToUpperInvariant();
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(full)))
            .ToLowerInvariant();

        return Path.Combine(RootFolder, hash + ".history.json");
    }

    private static string Shorten(string value)
    {
        value = (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ↵ ", StringComparison.Ordinal)
            .Trim();

        return value.Length <= 90
            ? value
            : value[..87] + "…";
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private sealed record FileHolder(string FilePath);
}
