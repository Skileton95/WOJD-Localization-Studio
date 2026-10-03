using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record SaveTransaction(int Schema, string Original, string Temp, string? BeforeHash, string AfterHash);
public sealed record SafetyEvent(DateTime AtUtc, string Operation, string FilePath, string Detail);
public static class SaveTransactionService
{
    private static string DirectoryPath => Path.Combine(WorkspaceStateService.StorageDirectory, "save-transactions");
    public static void Record(string operation, string file, string detail)
    {
        try { Directory.CreateDirectory(WorkspaceStateService.StorageDirectory); File.AppendAllText(Path.Combine(WorkspaceStateService.StorageDirectory, "safety.jsonl"), JsonSerializer.Serialize(new SafetyEvent(DateTime.UtcNow, operation, file, detail)) + "\n"); }
        catch (Exception e) { IssueLogService.Record("Журнал защиты: " + e.Message); }
    }
    public static string Prepare(LocalizationDocument doc, string temp)
    {
        Directory.CreateDirectory(DirectoryPath);
        var marker = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(marker, JsonSerializer.Serialize(new SaveTransaction(1, Path.GetFullPath(doc.FilePath), Path.GetFullPath(temp), doc.DiskHash, FileSafetyService.Hash(temp))));
        Record("save-prepared", doc.FilePath, temp); return marker;
    }
    public static void Commit(string temp, string destination)
    {
        if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
    }
    public static void Complete(string marker, string original)
    {
        Record("save-completed", original, marker);
        try { File.Delete(marker); } catch (IOException) { }
    }
    public static List<SafetyEvent> RecoverPending()
    {
        var report = new List<SafetyEvent>();
        if (!Directory.Exists(DirectoryPath)) return report;
        foreach (var marker in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            try
            {
                var tx = JsonSerializer.Deserialize<SaveTransaction>(File.ReadAllText(marker)) ?? throw new IOException("Пустой журнал сохранения.");
                if (tx.Schema != 1 || Path.GetFullPath(tx.Original) != tx.Original || Path.GetFullPath(tx.Temp) != tx.Temp ||
                    Path.GetDirectoryName(tx.Original) != Path.GetDirectoryName(tx.Temp) || !tx.Temp.StartsWith(tx.Original + ".wojd-", StringComparison.OrdinalIgnoreCase) ||
                    !tx.Temp.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || !new NdjsonLocalizationAdapter().CanOpen(tx.Original))
                    throw new IOException("Неподдерживаемая запись восстановления.");
                var originalHash = File.Exists(tx.Original) ? FileSafetyService.Hash(tx.Original) : null;
                if (originalHash == tx.AfterHash) { Complete(marker, tx.Original); continue; }
                if (originalHash == tx.BeforeHash && File.Exists(tx.Temp) && FileSafetyService.Hash(tx.Temp) == tx.AfterHash)
                {
                    Commit(tx.Temp, tx.Original); Complete(marker, tx.Original);
                    var item = new SafetyEvent(DateTime.UtcNow, "save-recovered", tx.Original, "Завершено прерванное сохранение.");
                    report.Add(item); Record(item.Operation, item.FilePath, item.Detail);
                }
                else
                {
                    var item = new SafetyEvent(DateTime.UtcNow, "manual-review", tx.Original, "Версия на диске изменилась или временный файл отсутствует. Перезапись заблокирована.");
                    report.Add(item); Record(item.Operation, item.FilePath, item.Detail);
                }
            }
            catch (Exception e) { report.Add(new(DateTime.UtcNow, "recovery-error", marker, e.Message)); Record("recovery-error", marker, e.Message); }
        }
        return report;
    }
    public static List<SafetyEvent> ReadJournal()
    {
        var path = Path.Combine(WorkspaceStateService.StorageDirectory, "safety.jsonl");
        if (!File.Exists(path)) return [];
        var rows = new List<SafetyEvent>();
        foreach (var line in File.ReadLines(path).TakeLast(1000))
            try { if (JsonSerializer.Deserialize<SafetyEvent>(line) is { } row) rows.Add(row); } catch (JsonException) { }
        return rows.AsEnumerable().Reverse().ToList();
    }
}