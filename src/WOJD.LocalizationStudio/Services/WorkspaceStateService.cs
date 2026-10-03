using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record WorkspaceState(List<string> OpenFiles, List<string> OpenFolders, string? ActiveFile, Dictionary<string, int> SelectedRows, List<DraftFileState> Drafts,
    List<string>? SearchHistory = null, List<string>? PinnedFiles = null, WindowLayoutState? Layout = null, List<NamespaceBookmark>? NamespaceFavorites = null,
    List<string>? ExpandedNamespaceFiles = null, bool NamespacePanelExpanded = true, List<SavedFilter>? SavedFilters = null, SmartFilter? SmartFilters = null,
    List<string>? ConsistencyExceptions = null, bool NotesOnly = false, List<DraftFileState>? BlockedRecovery = null);
public sealed record WindowLayoutState(double Left, double Top, double Width, double Height, bool Maximized, double FilesWidth = 300, bool FilesVisible = true);
public sealed record DraftFileState(string FilePath, List<DraftEntryState> Entries);
public sealed record DraftEntryState(int Index, string Namespace, string Key, string Translation, string? EditedNamespace = null, string? EditedKey = null, string? EditedOriginal = null, string? Source = null);
public static class WorkspaceStateService
{
    private static readonly string StateDirectory = Environment.GetEnvironmentVariable("WOJD_WORKSPACE_DIRECTORY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WOJD Localization Studio");
    public static string StorageDirectory => StateDirectory;
    private static string StatePath => Path.Combine(StateDirectory, "workspace.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static WorkspaceState Read(string path)
    {
        var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path), Options) ?? throw new JsonException("Пустое состояние.");
        return state with { OpenFiles = state.OpenFiles ?? [], OpenFolders = state.OpenFolders ?? [], SelectedRows = state.SelectedRows ?? [], Drafts = state.Drafts ?? [] };
    }
    private static void PreserveBroken()
    {
        if (File.Exists(StatePath)) File.Move(StatePath, StatePath + ".corrupt-" + Guid.NewGuid().ToString("N"));
    }
    public static WorkspaceState? Load()
    {
        try { if (File.Exists(StatePath)) return Read(StatePath); }
        catch (Exception e) { IssueLogService.Record("Workspace сохранён для ручного восстановления: " + e.Message); try { PreserveBroken(); } catch (Exception ex) { IssueLogService.Record(ex.Message); } }
        try { if (File.Exists(StatePath + ".last-good.bak")) { var state = Read(StatePath + ".last-good.bak"); SaveTransactionService.Record("workspace-recovered", StatePath, "Загружена последняя рабочая копия."); return state; } }
        catch (Exception e) { IssueLogService.Record("Копия workspace: " + e.Message); }
        return null;
    }
    public static void Save(WorkspaceState state)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            if (File.Exists(StatePath)) { try { _ = Read(StatePath); } catch (JsonException) { PreserveBroken(); } }
            ProjectMetadataService.Save(StatePath, state);
        } catch (Exception e) { IssueLogService.Record("Workspace не сохранён: " + e.Message); }
    }
}
