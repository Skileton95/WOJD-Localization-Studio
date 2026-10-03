using System.IO;
using System.Text.Json;
namespace WOJD.LocalizationStudio.Services;
public sealed record BinaryConversionTicket(int Schema, string OriginalPath, string OriginalSha256, string ExportPath, string ExportSha256, string Tool, string ToolVersion, DateTime VerifiedAtUtc, int Rows);
public static class BinaryWorkflowService
{
    public static void PreserveCopy(string original, string destination)
    {
        if (Path.GetFullPath(original).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) throw new IOException("Копия должна иметь другой путь.");
        File.Copy(original, destination, false);
        if (FileSafetyService.Hash(original) != FileSafetyService.Hash(destination)) throw new IOException("Хэш копии не совпал.");
    }
    public static async Task<BinaryConversionTicket> RegisterExportAsync(string original, string export, string tool, string version)
    {
        if (string.IsNullOrWhiteSpace(tool) || string.IsNullOrWhiteSpace(version)) throw new IOException("Укажите проверенный инструмент и его версию.");
        if (Path.GetFullPath(original).Equals(Path.GetFullPath(export), StringComparison.OrdinalIgnoreCase)) throw new IOException("Экспорт должен иметь другой путь.");
        if (!new NdjsonLocalizationAdapter().CanOpen(export)) throw new IOException("Поддерживается только NDJSON/JSONL-экспорт.");
        var before = FileSafetyService.Hash(original);
        var document = await new NdjsonLocalizationAdapter().LoadAsync(export);
        if (document.LoadIssues.Count > 0 || document.Entries.Count == 0) throw new IOException("Экспорт пустой или повреждён. Проверьте его внешним инструментом.");
        if (FileSafetyService.Hash(original) != before) throw new IOException("Оригинал изменился во время проверки.");
        return new(1, Path.GetFullPath(original), before, Path.GetFullPath(export), FileSafetyService.Hash(export), tool, version, DateTime.UtcNow, document.Entries.Count);
    }
    public static void SaveTicket(BinaryConversionTicket ticket, string path)
    {
        if (File.Exists(path) || Path.GetFullPath(path).Equals(ticket.OriginalPath, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path).Equals(ticket.ExportPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Выберите новый путь отчёта.");
        File.WriteAllText(path, JsonSerializer.Serialize(ticket, new JsonSerializerOptions { WriteIndented = true }));
    }
}
