using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Services;
public sealed record DiagnosticDocument(string Path, string Provider, bool ReadOnly, int Rows, int Changed, int Qa, int LoadIssues, string? LoadedHash);
public static class DiagnosticsService
{
    public static object Collect(MainViewModel vm) => new
    {
        AtUtc = DateTime.UtcNow, Version = vm.AppVersion, Runtime = Environment.Version.ToString(), OS = RuntimeInformation.OSDescription,
        WorkingSetMB = Process.GetCurrentProcess().WorkingSet64 / 1048576, Workspace = WorkspaceStateService.StorageDirectory,
        Project = vm.Project?.Name, GameVersion = vm.Project?.GameVersion,
        Documents = vm.OpenDocuments.Select(d => new DiagnosticDocument(d.FilePath, d.ProviderId, d.IsReadOnly, d.Entries.Count, d.Entries.Count(e => e.Status == WOJD.LocalizationStudio.Models.TranslationStatus.Modified), d.Entries.Count(e => e.HasValidationIssues), d.LoadIssues.Count, d.DiskHash)).ToList(),
        Sqlite = SqliteProjectIndexService.DatabasePath, Providers = new FormatProviderRegistry().Providers,
        Limits = "Нативного бинарного locres/fmtstring кодека нет; проверка в игре требует реальных файлов. Self-check использует только синтетический NDJSON."
    };
    public static void ExportNew(string path, object report)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
    }
    public static async Task SelfCheckAsync()
    {
        var directory = Path.Combine(WorkspaceStateService.StorageDirectory, "self-check", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "fixture.ndjson");
        await File.WriteAllTextAsync(file, "{\"namespace\":\"UI\",\"key\":\"test\",\"source\":\"原文 <Em>{Name}</>\",\"translation\":\"Перевод <Em>{Name}</>\",\"unknown\":42}");
        var providers = new FormatProviderRegistry(); var doc = await providers.LoadAsync(file);
        doc.Entries[0].Translation = "Проверено <Em>{Name}</>"; await providers.SaveAsync(doc);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        if (json.RootElement.GetProperty("unknown").GetInt32() != 42 || doc.HasFinalNewLine || WojdQaService.Analyze([doc], new()).Any(i => i.Severity == "Ошибка")) throw new IOException("Самопроверка NDJSON/QA не прошла.");
        await SqliteProjectIndexService.EnsureAsync(file, default, null);
        if (SqliteProjectIndexService.Read(file, default).Single().Entry.Translation != doc.Entries[0].Translation) throw new IOException("Самопроверка SQLite не прошла.");
        ProjectMetadataService.Save(Path.Combine(WorkspaceStateService.StorageDirectory, "self-check.json"), new { Success = true, AtUtc = DateTime.UtcNow, Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3), SqliteRows = 1, UnknownField = 42, FinalNewline = false, Fixture = file });
    }
}
