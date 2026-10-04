using System.IO;
using System.Text;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed record ProjectHistoryRecord(
    DateTimeOffset Timestamp,
    string FilePath,
    string Operation,
    int AffectedEntries,
    string Details,
    string? SnapshotPath = null);

public static class ProjectHistoryService
{
    private static readonly object Sync = new();
    private const int MaxRecords = 2000;

    private static string HistoryPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "project-history.jsonl");

    public static void Record(
        string filePath,
        string operation,
        int affectedEntries,
        string details = "",
        string? snapshotPath = null)
    {
        var record = new ProjectHistoryRecord(
            DateTimeOffset.Now,
            Path.GetFullPath(filePath),
            operation,
            Math.Max(0, affectedEntries),
            details ?? string.Empty,
            snapshotPath);

        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            File.AppendAllText(
                HistoryPath,
                JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false));

            TrimIfNeeded();
        }
    }

    public static IReadOnlyList<ProjectHistoryRecord> LoadRecent(
        string? filePath = null,
        int limit = 300)
    {
        lock (Sync)
        {
            if (!File.Exists(HistoryPath))
                return [];

            var fullPath = string.IsNullOrWhiteSpace(filePath)
                ? null
                : Path.GetFullPath(filePath);

            var rows = new List<ProjectHistoryRecord>();
            foreach (var line in File.ReadLines(HistoryPath, Encoding.UTF8))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<ProjectHistoryRecord>(line, JsonOptions);
                    if (record is null)
                        continue;

                    if (fullPath is not null &&
                        !string.Equals(
                            Path.GetFullPath(record.FilePath),
                            fullPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    rows.Add(record);
                }
                catch
                {
                }
            }

            return rows
                .OrderByDescending(x => x.Timestamp)
                .Take(Math.Clamp(limit, 1, 2000))
                .ToList();
        }
    }

    private static void TrimIfNeeded()
    {
        try
        {
            var lines = File.ReadAllLines(HistoryPath, Encoding.UTF8);
            if (lines.Length <= MaxRecords)
                return;

            File.WriteAllLines(
                HistoryPath,
                lines[^MaxRecords..],
                new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
