using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed record ChangeHistoryRecord(
    DateTime Utc,
    string FilePath,
    int RowIndex,
    string Namespace,
    string Key,
    string Before,
    string After,
    string OperationId,
    string OperationName);

public static class ChangeHistoryService
{
    public static void Append(
        string rootPath,
        ChangeHistoryRecord record)
    {
        try
        {
            var path =
                GetHistoryPath(rootPath);

            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);

            File.AppendAllText(
                path,
                JsonSerializer.Serialize(record) +
                Environment.NewLine);
        }
        catch
        {
        }
    }

    public static List<ChangeHistoryRecord> Read(
        string rootPath,
        int limit = 5000)
    {
        var result =
            new List<ChangeHistoryRecord>();

        try
        {
            var path =
                GetHistoryPath(rootPath);

            if (!File.Exists(path))
                return result;

            foreach (var line in
                     File.ReadLines(path)
                         .Reverse()
                         .Take(limit))
            {
                try
                {
                    var record =
                        JsonSerializer.Deserialize<ChangeHistoryRecord>(
                            line);

                    if (record is not null)
                        result.Add(record);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return result;
    }

    public static void Prune(
        string rootPath,
        int maxRecords)
    {
        try
        {
            var path =
                GetHistoryPath(rootPath);

            if (!File.Exists(path))
                return;

            var lines =
                File.ReadLines(path)
                    .TakeLast(
                        Math.Max(maxRecords, 1000))
                    .ToArray();

            File.WriteAllLines(
                path,
                lines);
        }
        catch
        {
        }
    }

    private static string GetHistoryPath(string rootPath)
        => Path.Combine(
            Path.GetFullPath(rootPath),
            ".wojd-studio",
            "history.jsonl");
}
