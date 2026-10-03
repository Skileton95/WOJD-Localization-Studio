using System.Windows;
using System.Windows.Threading;
using WOJD.LocalizationStudio.ViewModels;
using WOJD.LocalizationStudio.Views;
internal static class WorkflowSmokeChecks
{
    public static async Task Run(MainViewModel vm)
    {
        Func<Window>[] windows = [
            () => new WojdProjectWindow(vm), () => new BinaryWorkflowWindow(vm), () => new ImportWorkflowWindow(vm),
            () => new CollisionWindow(vm), () => new PatchMigrationWindow(vm), () => new GlossaryWindow(vm),
            () => new TerminologyQaWindow(vm), () => new WojdQaWindow(vm), () => new ReviewQueueWindow(vm),
            () => new ReleasePackageWindow(vm), () => new GitWindow(vm), () => new CollaborationWindow(vm),
            () => new ContextWindow(vm), () => new SqliteIndexWindow(vm), () => new DiagnosticsWindow(vm),
            () => new SettingsWindow(), () => new ExpandedEditorWindow(vm), () => new ProjectReplaceWindow(vm),
            () => new PatchSyncWindow(vm), () => new ConsistencyWindow(vm), () => new NotesWindow(vm),
            () => new ProjectStatisticsWindow(vm), () => new EditHistoryWindow(vm), () => new SaveHistoryWindow(vm),
            () => new SmartFiltersWindow(vm), () => new RecoveryConflictsWindow(vm)];
        foreach (var create in windows)
        {
            var window = create(); window.ShowInTaskbar = false; window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -10000; window.Top = -10000;
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(40);
            Program.Check(window.IsLoaded && window.Content is not null, "Workflow window loads: " + window.Title);
            window.Close();
        }
        Console.WriteLine("PASS 26 WPF workflow windows");
    }
}
