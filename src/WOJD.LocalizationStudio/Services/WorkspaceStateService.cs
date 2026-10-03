using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record WorkspaceState(
    List<string> OpenFiles,
    List<string> OpenFolders,
    string? ActiveFile,
    Dictionary<string, int> SelectedRows,
    List<DraftFileState> Drafts,
    List<string>? SearchHistory = null,
    List<string>? PinnedFiles = null,
    WindowLayoutState? Layout = null,
    List<NamespaceBookmark>? NamespaceFavorites = null,
    List<string>? ExpandedNamespaceFiles = null,
    bool NamespacePanelExpanded = true,
    List<SavedFilter>? SavedFilters = null,
    SmartFilter? SmartFilters = null,
    List<string>? ConsistencyExceptions = null,
    bool NotesOnly = false);

public sealed record WindowLayoutState(double Left, double Top, double Width, double Height,
    bool Maximized, double FilesWidth = 300, bool FilesVisible = true);

public sealed record DraftFileState(
    string FilePath,
    List<DraftEntryState> Entries);

public sealed record DraftEntryState(
    int Index,
    string Namespace,
    string Key,
    string Translation,
    string? EditedNamespace = null,
    string? EditedKey = null,
    string? EditedOriginal = null,
    string? Source = null);

public static class WorkspaceStateService
{
    private static readonly string StateDirectory =
        Environment.GetEnvironmentVariable("WOJD_WORKSPACE_DIRECTORY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WOJD Localization Studio");

    public static string StorageDirectory => StateDirectory;

    private static readonly string StatePath =
        Path.Combine(StateDirectory, "workspace.json");

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public static WorkspaceState? Load()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;

            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<WorkspaceState>(
                json,
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WorkspaceState state)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);

            var tempPath = StatePath + ".tmp";
            var json = JsonSerializer.Serialize(
                state,
                JsonOptions);

            File.WriteAllText(tempPath, json);
            File.Move(tempPath, StatePath, true);
        }
        catch
        {
            // Workspace recovery must never block the editor.
        }
    }
}
