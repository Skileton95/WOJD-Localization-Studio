using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
internal static class ProtectionChecks
{
    public static async Task Run(string folder)
    {
        var adapter = new NdjsonLocalizationAdapter(); var mixed = Path.Combine(folder, "mixed.ndjson");
        var line1 = "{\"key\":\"first\",\"source\":\"原文\",\"translation\":\"\"}";
        var line2 = "{\"key\":\"second\",\"source\":\"原文\",\"translation\":\"\"}";
        var line3 = "{\"key\":\"last\",\"source\":\"原文\",\"translation\":\"\"}";
        await File.WriteAllTextAsync(mixed, line1 + "\n\n" + line2 + "\r\n" + line3);
        var doc = await adapter.LoadAsync(mixed); doc.Entries[0].Translation = "Перевод"; await adapter.SaveAsync(doc);
        Program.Check((await File.ReadAllTextAsync(mixed)).EndsWith("\n\n" + line2 + "\r\n" + line3) && !doc.HasFinalNewLine, "Mixed physical line endings and missing final newline");
        var utf = Path.Combine(folder, "restore-utf16.ndjson"); await File.WriteAllTextAsync(utf, line1, new UnicodeEncoding(false, true, true));
        BackupService.CreateBackup(utf); var backup = BackupService.List(utf).Single();
        var restore = new MainViewModel(); await restore.LoadPathAsync(utf);
        restore.SelectedEntry!.Translation = "Черновик"; await restore.RestoreBackupAsync(backup.Path);
        Program.Check(restore.SelectedEntry!.Translation == "" && (await File.ReadAllBytesAsync(utf))[0] == 255 && !restore.ActiveDocument!.HasFinalNewLine, "Full .bak restoration preserves UTF16 and final newline");
        Program.Check(BackupService.List(utf).Count >= 3, "Restore preserves disk and dirty snapshot backups");
        var beforeExport = FileSafetyService.Hash(utf); var exportBlocked = false;
        try { await NdjsonExportService.ExportAsync(restore.ActiveDocument!.Entries, utf); } catch (IOException) { exportBlocked = true; }
        Program.Check(exportBlocked && FileSafetyService.Hash(utf) == beforeExport, "Export cannot overwrite original");
        var settings = new EditorSettings { Shortcuts = [new() { Action = "Save", Gesture = "Ctrl+F" }] };
        AppSettingsService.Validate(settings);
        Program.Check(settings.Shortcuts.Single(s => s.Action == "Search").Gesture == "", "Legacy shortcuts migration does not duplicate custom gestures");
        var workspace = new WorkspaceState([utf], [], utf, [], []);
        WorkspaceStateService.Save(workspace); WorkspaceStateService.Save(workspace);
        await File.WriteAllTextAsync(Path.Combine(WorkspaceStateService.StorageDirectory, "workspace.json"), "{broken}");
        Program.Check(WorkspaceStateService.Load()?.ActiveFile == utf && Directory.EnumerateFiles(WorkspaceStateService.StorageDirectory, "workspace.json.corrupt-*").Any(), "Broken workspace preserved and last-good recovered");
        var legacy = workspace with { Drafts = [new(utf, [new(1, "", "first", "Старый черновик")])] };
        WorkspaceStateService.Save(legacy); var recovering = new MainViewModel(); await recovering.RestoreWorkspaceAsync();
        Program.Check(recovering.RecoveryConflicts.Count == 1 && recovering.SelectedEntry!.Translation == "", "Legacy recovery without source needs manual review");
        var cache = await ProjectSearchIndexService.EnsureAsync(utf, default, null);
        await using (var zip = new GZipStream(File.Create(cache), CompressionMode.Compress))
        await using (var writer = new StreamWriter(zip, new UTF8Encoding(false))) { await writer.WriteLineAsync(JsonSerializer.Serialize(new SearchIndexHeader(1, FileSafetyService.Hash(utf)))); await writer.WriteLineAsync("{broken}"); }
        await SqliteProjectIndexService.ResetAsync(); await SqliteProjectIndexService.EnsureAsync(utf, default, null);
        Program.Check(SqliteProjectIndexService.Read(utf, default).Count() == 1, "Corrupt compressed index rebuilt without source changes");
        Console.WriteLine("PASS protection/mixed endings/UTF16 backup/export/workspace/recovery/cache");
    }
}
