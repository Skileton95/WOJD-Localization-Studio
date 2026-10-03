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
    private static async Task Performance(int rows, bool withVm)
    {
        var path = Path.Combine(Path.GetTempPath(), "wojd-perf-" + Guid.NewGuid() + ".ndjson");
        Environment.SetEnvironmentVariable("WOJD_WORKSPACE_DIRECTORY", path + ".workspace");
        await using (var writer = new StreamWriter(path))
            for (var i = 0; i < rows; i++) await writer.WriteLineAsync($"{{\"namespace\":\"UI\",\"key\":\"k{i}\",\"source\":\"原文 {i}\",\"translation\":\"Перевод {i}\",\"extra\":42}}");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var adapter = new NdjsonLocalizationAdapter(); var document = await adapter.LoadAsync(path);
        Check(document.Entries.Count == rows && document.Entries[^1].Key == "k" + (rows - 1), "Large-file loading");
        Console.WriteLine($"PERF load rows={rows:N0} seconds={clock.Elapsed.TotalSeconds:F2} managedMB={GC.GetTotalMemory(false) / 1048576.0:F1}");
        document.Entries[^1].Translation = "Проверено"; await adapter.SaveAsync(document);
        Check(document.Entries[^1].Status == TranslationStatus.Translated, "Large-file save");
        Console.WriteLine($"PERF load+save seconds={clock.Elapsed.TotalSeconds:F2}");
        if (withVm)
        {
            document = null!; GC.Collect(); var vm = new MainViewModel(); clock.Restart(); await vm.LoadPathAsync(path);
            Check(vm.TotalCount == rows && vm.Namespaces[0].Total == rows && vm.OpenTabs.Count == 1, "Large project session and namespace cache");
            Console.WriteLine($"PERF session rows={rows:N0} seconds={clock.Elapsed.TotalSeconds:F2} managedMB={GC.GetTotalMemory(false) / 1048576.0:F1}");
        }
    }
    private static async Task Run(string[] args)
    {
        if (args.Length == 2 && (args[0] == "--perf" || args[0] == "--perf-vm")) { await Performance(int.Parse(args[1]), args[0] == "--perf-vm"); return; }
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
        Check(ConsistencyService.Analyze(new[] { vm.ActiveDocument!, duplicate }).Duplicates.Count == 1, "Identical translations still form a duplicate group");
        Check(vm.ToggleConsistencyException(first.Original) && !vm.ToggleConsistencyException(first.Original), "Intentional consistency exceptions");
        Check(vm.NamespaceTree.Count == 2, "Namespace tree must include both files");
        vm.ToggleNamespaceFavorite(vm.Namespaces[0]); Check(vm.Namespaces[0].Favorite, "Namespace favorites");
        var smartRow = new LocalizationEntry { Original = "中文 {0}", Translation = "" };
        Check(new SmartFilter(Untranslated: true, Chinese: true, Placeholders: true).Matches(smartRow), "Combined smart filters");
        Check(!new SmartFilter(Untranslated: true, Latin: true).Matches(smartRow), "Conditions must combine with AND");
        vm.SaveFilter("test", new SmartFilter(Chinese: true)); Check(vm.SavedFilters.Count == 1, "Saved filter");
        vm.SetAnnotation(first, "Проверить контекст", "Контекст неизвестен");
        Check(vm.GetAnnotation(first)?.Note == "Проверить контекст" && !first.RawLine.Contains("контекст"), "Notes stay outside game files");
        var projectStats = ProjectStatisticsService.Calculate(vm.OpenDocuments);
        Check(projectStats.Files == 2 && projectStats.Rows == 2 && projectStats.Translated == 2, "Project statistics count modified translations");
        Check(ProjectStatisticsService.ToCsv(projectStats).Contains("Progress"), "CSV statistics export");
        var replacementRows = ProjectReplaceService.Preview(vm.OpenDocuments, EntryField.Namespace, "UI", "HUD", false, true, true);
        Check(replacementRows.Count == 2, "Project namespace preview");
        vm.ApplyBatch(replacementRows.Select(x => (x.Entry, x.Field, x.Before, x.After)));
        Check(vm.OpenDocuments.All(d => d.Entries[0].Namespace == "HUD"), "Project replacement");
        Check(vm.EditHistory[0].Count == 2 && vm.EditHistory[0].Rows.All(x => x.Before == "UI"), "One history record per batch");
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
        Check(TokenSyntaxService.Analyze("{0} %s <b>", "{0} %d <b>").Count(x => x.Mismatch) == 1, "Inline token differences");
        var indexPath = await ProjectSearchIndexService.EnsureAsync(b, default, null);
        var indexed = ProjectSearchService.SearchLocations(ProjectSearchIndexService.Read(indexPath, b, default), "原文", true, true, false);
        Check(indexed.Results.Count == 1 && indexed.Results[0].Key == "new-key", "Separate search index");
        vm.FileTree.Add(new FileNode { Name = "corrupt.ndjson", FullPath = corrupt });
        var streamed = await vm.SearchProjectStreamingAsync("Перевод", true, true, false, default, null);
        Check(streamed.Results.Count == 1 && vm.OpenTabs.Count == 2, "Search must not open every project file");
        await vm.OpenProjectSearchResultAsync(streamed.Results[0]); Check(vm.OpenTabs.Count == 3 && vm.SelectedEntry!.Key == "k", "Navigation loads only selected file");
        var transactionFile = Path.Combine(folder, "transaction.ndjson");
        await File.WriteAllTextAsync(transactionFile, "{\"key\":\"a\",\"translation\":\"old\"}\n");
        var txDoc = await adapter.LoadAsync(transactionFile); var txTemp = transactionFile + ".wojd-test.tmp";
        await File.WriteAllTextAsync(txTemp, "{\"key\":\"a\",\"translation\":\"new\"}\n");
        _ = SaveTransactionService.Prepare(txDoc, txTemp);
        Check(SaveTransactionService.RecoverPending().Any(x => x.Operation == "save-recovered"), "Interrupted save recovery");
        Check((await adapter.LoadAsync(transactionFile)).Entries[0].Translation == "new", "Recovered save content");
        var project = new WojdProject { ManifestPath = Path.Combine(folder, "test.wojd-project.json"), Sources = [new() { Path = "a.ndjson", Role = "CN" }, new() { Path = "b.ndjson", Role = "RU" }] };
        WojdProjectService.Save(project); var loadedProject = WojdProjectService.Load(project.ManifestPath);
        Check((await WojdProjectService.ReadAsync(loadedProject)).Rows.Count == 2, "WOJD project roles and unified rows");
        var escaped = false; try { WojdProjectService.Resolve(project, "../escape.ndjson"); } catch (IOException) { escaped = true; }
        Check(escaped, "Project paths must stay inside root");
        var binary = Path.Combine(folder, "sample.locres"); await File.WriteAllBytesAsync(binary, [0, 255, 42]);
        var binaryHash = FileSafetyService.Hash(binary); BinaryWorkflowService.PreserveCopy(binary, binary + ".copy");
        var ticket = await BinaryWorkflowService.RegisterExportAsync(binary, b, "fixture-converter", "test");
        Check(ticket.Rows == 1 && ticket.OriginalSha256 == binaryHash && FileSafetyService.Hash(binary) == binaryHash, "Binary workflow preserves original bytes");
        var importTarget = new LocalizationDocument { FilePath = "target" }; var importOld = new LocalizationDocument { FilePath = "incoming" };
        importTarget.Entries.Add(new() { Namespace = "UI", Key = "x", Original = "同", Translation = "мой" });
        importOld.Entries.Add(new() { Namespace = "UI", Key = "x", Original = "同", Translation = "новый" });
        Check(ProjectImportService.Preview([importTarget], [importOld], "RU", false).Single().Kind == "Защищена", "Import protects existing translation");
        Check(ProjectImportService.Changes(ProjectImportService.Preview([importTarget], [importOld], "RU", true)).Count() == 1, "Explicit import overwrite preview");
        importOld.Entries[0].Original = "different"; Check(!ProjectImportService.Preview([importTarget], [importOld], "RU", true).Single().Allowed, "Import changed-source guard");
        importTarget.Entries.Add(new() { Index = 2, Namespace = "UI", Key = "x", Original = "同", Translation = "other" });
        var choice = new CollisionChoice("UI", "x", "target", 2, CollisionService.SourceHash("同"), "Ручной выбор", DateTime.UtcNow);
        Check(CollisionService.Resolve([importTarget], [choice])[("UI", "x")].Entry.Index == 2, "Explicit collision candidate");
        importTarget.Entries[1].Original = "changed";
        Check(CollisionService.Resolve([importTarget], [choice]).Count == 0, "Stale collision choice must not resolve");
        var migrationTarget = new LocalizationDocument { FilePath = "patch" };
        migrationTarget.Entries.Add(new() { Namespace = "UI", Key = "moved", Original = "独特原文", Translation = "" });
        var migrationOld = new LocalizationDocument { FilePath = "previous" };
        migrationOld.Entries.Add(new() { Namespace = "OLD", Key = "old", Original = "独特原文", Translation = "Уникальный" });
        Check(PatchMigrationService.Preview([migrationTarget], [migrationOld], false).Single().Confirmed, "Unique unchanged source migration across renamed keys");
        Check(FileComparisonService.Compare(importTarget, importTarget).Items.All(x => !x.CanTransfer), "Legacy compare blocks collisions");
        var term = new GlossaryTerm { Chinese = "独特", Russian = "Уникальный", Namespace = "UI" };
        GlossaryService.Save(Path.Combine(folder, "glossary.json"), [term]);
        Check(GlossaryService.Hints(GlossaryService.Load(Path.Combine(folder, "glossary.json")), migrationTarget.Entries[0]).Contains("Уникальный"), "Glossary Unicode persistence and namespace hints");
        migrationTarget.Entries[0].Translation = "Неуникальный";
        Check(TerminologyQaService.Analyze([migrationTarget], [term], []).Count == 1, "Term QA respects word boundaries");
        var termIssue = TerminologyQaService.Analyze([migrationTarget], [term], []).Single(); var termException = TerminologyQaService.Except(termIssue, "Контекст");
        Check(TerminologyQaService.Analyze([migrationTarget], [term], [termException]).Count == 0, "Reasoned term exception");
        migrationTarget.Entries[0].Translation = "Иное";
        Check(TerminologyQaService.Analyze([migrationTarget], [term], [termException]).Count == 1, "Translation change invalidates term exception");
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