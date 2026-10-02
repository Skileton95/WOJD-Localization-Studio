using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed record WorkspaceState(
    List<string> OpenFiles,
    List<string> OpenFolders,
    string? ActiveFile,
    Dictionary<string, int> SelectedRows,
    List<DraftFileState> Drafts);

public sealed record DraftFileState(
    string FilePath,
    List<DraftEntryState> Entries);

public sealed record DraftEntryState(
    int Index,
    string Namespace,
    string Key,
    string Translation);

public static class WorkspaceStateService
{
    private static readonly string StateDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD Localization Studio");

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
