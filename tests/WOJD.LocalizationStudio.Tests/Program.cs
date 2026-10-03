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
        var vm = new MainViewModel();
        await vm.LoadPathAsync(a);
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
        Console.WriteLine("PASS tabs, pinning, independent undo, UTF-8/unknown-field roundtrip, legacy workspace");
        // Leave test files in the isolated temp folder for failure diagnosis.
    }
}