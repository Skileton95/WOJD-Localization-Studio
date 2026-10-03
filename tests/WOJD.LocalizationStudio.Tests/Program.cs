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
    public static int Main()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Run(); Console.WriteLine("All regression checks passed."); }
            catch (Exception e) { Console.Error.WriteLine(e); _exit = 1; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        return _exit;
    }
    private static async Task Run()
    {
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