using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

internal static class Program
{
    private static int _exit;
    public static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [STAThread]
    public static int Main(string[] args)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Run(args); Console.WriteLine("All regression checks passed."); }
            catch (Exception e) { Console.Error.WriteLine(e); _exit = 1; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        return _exit;
    }
    private static async Task Performance(int rows)
    {
        var path = Path.Combine(Path.GetTempPath(), "wojd-perf-" + Guid.NewGuid() + ".ndjson");
        await using (var writer = new StreamWriter(path))
            for (var i = 0; i < rows; i++) await writer.WriteLineAsync($"{{\"namespace\":\"UI\",\"key\":\"k{i}\",\"source\":\"原文 {i}\",\"translation\":\"Перевод {i}\",\"extra\":42}}");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var adapter = new NdjsonLocalizationAdapter(); var document = await adapter.LoadAsync(path);
        Check(document.Entries.Count == rows && document.Entries[^1].Key == "k" + (rows - 1), "Large-file loading");
        Console.WriteLine($"PERF load rows={rows:N0} seconds={clock.Elapsed.TotalSeconds:F2} managedMB={GC.GetTotalMemory(false) / 1048576.0:F1}");
        document.Entries[^1].Translation = "Проверено"; await adapter.SaveAsync(document);
        Check(document.Entries[^1].Status == TranslationStatus.Translated, "Large-file save");
        Console.WriteLine($"PERF load+save seconds={clock.Elapsed.TotalSeconds:F2}");
    }
    private static async Task Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--perf") { await Performance(int.Parse(args[1])); return; }
        var folder = Path.Combine(Path.GetTempPath(), "wojd-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var a = Path.Combine(folder, "a.ndjson");
        var b = Path.Combine(folder, "b.ndjson");
        await File.WriteAllTextAsync(a, "{\"namespace\":\"UI\",\"key\":\"a\",\"source\":\"你好 {0}\",\"translated\":\"Привет {0}\",\"unknown\":42}\n");
        await File.WriteAllTextAsync(b, "{\"namespace\":\"UI\",\"key\":\"b\",\"source\":\"再见\",\"translation\":\"До свидания\"}\n");
        Environment.SetEnvironmentVariable("WOJD_WORKSPACE_DIRECTORY", folder);
        var vm = new MainViewModel();
        await vm.LoadPathAsync(a);
        Check(vm.Namespaces.Single().Total == 1 && vm.Namespaces.Single().Translated == 1, "Namespace statistics");
        vm.SelectedEntry!.Translation = "Здравствуйте {0}";
        var first = vm.SelectedEntry;
        vm.TogglePin(vm.OpenTabs[0]);
        await vm.LoadPathAsync(b);
        Check(vm.OpenTabs.Count == 2, "Two files should remain open");
        vm.CycleTab(1);
        Check(ReferenceEquals(vm.SelectedEntry, first) && vm.OpenTabs[0].IsPinned, "Tab switch must retain edits, selection and pin");
        vm.UndoCommand.Execute(null);
        Check(first.Translation == "Привет {0}", "Undo must stay in the file session");
        vm.RedoCommand.Execute(null);
        var adapter = new NdjsonLocalizationAdapter();
        await adapter.SaveAsync(vm.ActiveDocument!);
        using var parsed = JsonDocument.Parse((await File.ReadAllTextAsync(a)).Trim());
        Check(parsed.RootElement.GetProperty("unknown").GetInt32() == 42, "Unknown fields must survive saving");
        Check(parsed.RootElement.GetProperty("translated").GetString() == "Здравствуйте {0}", "Original translation field must survive");
        Check(parsed.RootElement.GetProperty("source").GetString() == "你好 {0}", "Original text must survive");
        var state = JsonSerializer.Deserialize<WorkspaceState>("{\"OpenFiles\":[],\"OpenFolders\":[],\"SelectedRows\":{},\"Drafts\":[]}");
        Check(state is { Layout: null, PinnedFiles: null }, "Existing v0.1.23 workspace must remain readable");
        Check(TranslationValidator.Validate("你好 {0}", "Привет {0}").IssueCount == 0, "QA placeholder matching");
        var duplicate = new LocalizationDocument { FilePath = "duplicates" };
        var other = new LocalizationEntry { Original = first.Original, Key = "other", Translation = "Другой {0}" };
        duplicate.Entries.Add(other);
        var consistency = ConsistencyService.Analyze(new[] { vm.ActiveDocument!, duplicate });
        Check(consistency.Conflicts.Count == 1 && consistency.Conflicts[0].Count == 2, "Conflicting translations across files");
        other.Translation = first.Translation;
        Check(ConsistencyService.Analyze(new[] { vm.ActiveDocument!, duplicate }).Conflicts.Count == 0, "Resolved consistency");
        Check(vm.NamespaceTree.Count == 2, "Namespace tree must include both files");
        vm.ToggleNamespaceFavorite(vm.Namespaces[0]); Check(vm.Namespaces[0].Favorite, "Namespace favorites");
        var smartRow = new LocalizationEntry { Original = "中文 {0}", Translation = "" };
        Check(new SmartFilter(Untranslated: true, Chinese: true, Placeholders: true).Matches(smartRow), "Combined smart filters");
        Check(!new SmartFilter(Untranslated: true, Latin: true).Matches(smartRow), "Conditions must combine with AND");
        vm.SaveFilter("test", new SmartFilter(Chinese: true)); Check(vm.SavedFilters.Count == 1, "Saved filter");
        var replacementRows = ProjectReplaceService.Preview(vm.OpenDocuments, EntryField.Namespace, "UI", "HUD", false, true, true);
        Check(replacementRows.Count == 2, "Project namespace preview");
        vm.ApplyBatch(replacementRows.Select(x => (x.Entry, x.Field, x.Before, x.After)));
        Check(vm.OpenDocuments.All(d => d.Entries[0].Namespace == "HUD"), "Project replacement");
        vm.UndoCommand.Execute(null);
        Check(vm.OpenDocuments.All(d => d.Entries[0].Namespace == "UI"), "Single Undo across files");
        vm.RedoCommand.Execute(null);
        Check(vm.OpenDocuments.All(d => d.Entries[0].Namespace == "HUD"), "Single Redo across files");
        vm.UndoCommand.Execute(null);
        var identityDoc = await adapter.LoadAsync(b);
        identityDoc.Entries[0].Namespace = "测试"; identityDoc.Entries[0].Key = "new-key"; identityDoc.Entries[0].Original = "原文";
        await adapter.SaveAsync(identityDoc);
        var reread = await adapter.LoadAsync(b);
        Check(reread.Entries[0].Namespace == "测试" && reread.Entries[0].Key == "new-key" && reread.Entries[0].Original == "原文", "Identity/source roundtrip");
        var newPatch = new LocalizationDocument { FilePath = "new" };
        var patchEntry = new LocalizationEntry { Namespace = first.Namespace, Key = first.Key, Original = first.Original };
        patchEntry.InitializeSavedTranslation(""); newPatch.Entries.Add(patchEntry);
        var sync = PatchSyncService.Preview(new[] { newPatch }, new[] { vm.ActiveDocument! });
        Check(sync.Single().CanTransfer && PatchSyncService.Transfers(sync, false).Count() == 1, "Exact patch migration");
        patchEntry.Original = "changed";
        Check(PatchSyncService.Preview(new[] { newPatch }, new[] { vm.ActiveDocument! }).Single().Kind == "Оригинал изменён", "Changed source blocks transfer");
        newPatch.Entries.Add(new LocalizationEntry { Namespace = first.Namespace, Key = first.Key, Original = first.Original });
        Check(PatchSyncService.Preview(new[] { newPatch }, new[] { vm.ActiveDocument! }).All(x => !x.CanTransfer), "Collisions must block automatic migration");
        BackupService.CreateBackup(a); BackupService.CreateBackup(a);
        Check(BackupService.List(a).Count == 2, "Backups in the same second must be distinct");
        BackupService.Prune(a, 1); Check(BackupService.List(a).Count == 1, "Backup retention");
        var stale = await adapter.LoadAsync(a); stale.Entries[0].Translation = "Локально";
        await File.AppendAllTextAsync(a, "\n");
        var rejected = false; try { await adapter.SaveAsync(stale); } catch (IOException) { rejected = true; }
        Check(rejected && (await adapter.LoadAsync(a)).Entries[0].Translation == "Здравствуйте {0}", "External edits must block overwrite");
        var corrupt = Path.Combine(folder, "corrupt.ndjson");
        await File.WriteAllTextAsync(corrupt, "{broken}\n\n{\"namespace\":\"x\",\"key\":\"k\",\"source\":\"原文\",\"translation\":\"\"}\n[]\n");
        var diagnostic = await adapter.LoadAsync(corrupt);
        Check(diagnostic.LoadIssues.Count == 2 && diagnostic.Entries[0].LineNumber == 3, "Malformed physical-line diagnostics");
        diagnostic.Entries[0].Translation = "Перевод"; await adapter.SaveAsync(diagnostic);
        var persisted = await File.ReadAllTextAsync(corrupt);
        Check(persisted.StartsWith("{broken}\n\n") && persisted.EndsWith("[]\n"), "Malformed/blank lines must survive save");
        var utf16 = Path.Combine(folder, "utf16.ndjson");
        await File.WriteAllTextAsync(utf16, "{\"key\":\"k\",\"source\":\"原文\",\"translation\":\"\"}\r\n", new System.Text.UnicodeEncoding(false, true, true));
        var unicode = await adapter.LoadAsync(utf16); unicode.Entries[0].Translation = "Перевод"; await adapter.SaveAsync(unicode);
        var rawUnicode = await File.ReadAllBytesAsync(utf16); Check(rawUnicode[0] == 255 && rawUnicode[1] == 254 && unicode.NewLine == "\r\n", "UTF-16 BOM and line ending preservation");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var canceled = false; try { await adapter.LoadAsync(corrupt, cancel.Token); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "Loading is cancelable");
        AppSettingsService.Save(new EditorSettings { FontSize = 16, BackupLimit = 3 });
        Check(AppSettingsService.Load().BackupLimit == 3, "Settings persistence"); AppSettingsService.Save(new());
        var app = new WOJD.LocalizationStudio.App(); app.InitializeComponent();
        var window = new WOJD.LocalizationStudio.MainWindow();
        window.DataContext = vm;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var content = (System.Windows.FrameworkElement)window.Content;
        content.Measure(new System.Windows.Size(1540,980)); content.Arrange(new System.Windows.Rect(0,0,1540,980));
        content.UpdateLayout();
        Check(((System.Windows.Controls.DataGrid)window.FindName("EntriesGrid")).Items.Count == 1, "Table should bind the active session");
        if (Environment.GetEnvironmentVariable("WOJD_TEST_RENDER") is { } renderPath)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1540, 980, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(content);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = File.Create(renderPath); png.Save(output);
        }
        Console.WriteLine("PASS tabs, pinning, independent undo, UTF-8/unknown-field roundtrip, legacy workspace");
        // Leave test files in the isolated temp folder for failure diagnosis.
    }
}