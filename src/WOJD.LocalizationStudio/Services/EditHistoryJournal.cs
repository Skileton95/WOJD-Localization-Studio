using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public static class EditHistoryJournal
{
    public static string PathFor(string directory) => Path.Combine(directory, "edit-history.jsonl");
    public static void Append(string directory, EditHistoryItem item)
    {
        try { Directory.CreateDirectory(directory); File.AppendAllText(PathFor(directory), JsonSerializer.Serialize(item) + "\n"); }
        catch (Exception e) { IssueLogService.Record("История: " + e.Message); }
    }
    public static List<EditHistoryItem> Read(string directory)
    {
        var path = PathFor(directory); if (!File.Exists(path)) return [];
        var result = new List<EditHistoryItem>();
        foreach (var line in File.ReadLines(path).TakeLast(2000))
            try { if (JsonSerializer.Deserialize<EditHistoryItem>(line) is { } item && item.Rows is not null) result.Add(item); }
            catch (JsonException e) { IssueLogService.Record("Повреждённая запись истории сохранена: " + e.Message); }
        return result.OrderByDescending(i => i.AtUtc).ToList();
    }
}
