using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public async Task RestoreWojdProjectAsync()
    {
        var path = System.IO.Path.Combine(WorkspaceStateService.StorageDirectory, "last-project.txt");
        if (!System.IO.File.Exists(path)) return;
        try { var manifest = System.IO.File.ReadAllText(path); if (System.IO.File.Exists(manifest)) await OpenWojdProjectAsync(manifest); }
        catch (Exception e) { IssueLogService.Record("Проект: " + e.Message); }
    }
    public async Task<IReadOnlyList<WOJD.LocalizationStudio.Models.LocalizationDocument>> WojdTargetsAsync()
    {
        if (Project is null) { await EnsureProjectLoadedAsync(); return OpenDocuments.ToList(); }
        await OpenWojdProjectAsync(Project.ManifestPath);
        var paths = Project.Sources.Where(s => s.Role == "RU" && s.Format != "locres").Select(s => WojdProjectService.Resolve(Project, s.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return OpenDocuments.Where(d => paths.Contains(d.FilePath)).ToList();
    }
    public string ProjectDataDirectory => Project is null ? WorkspaceStateService.StorageDirectory : System.IO.Path.Combine(Project.Root, ".wojd-studio");
    public WojdProject? Project { get; private set; }
    public async Task OpenWojdProjectAsync(string path)
    {
        var project = WojdProjectService.Load(path);
        Project = project;
        _adapter.DeclareProject(project);
        RestoreEditHistory();
        System.IO.Directory.CreateDirectory(WorkspaceStateService.StorageDirectory);
        System.IO.File.WriteAllText(System.IO.Path.Combine(WorkspaceStateService.StorageDirectory, "last-project.txt"), project.ManifestPath);
        foreach (var source in project.Sources.Where(s => s.Role == "RU" && s.Format != "locres"))
        {
            var file = WojdProjectService.Resolve(project, source.Path);
            if (System.IO.File.Exists(file) && new NdjsonLocalizationAdapter().CanOpen(file)) { await LoadPathAsync(file); var document = OpenDocuments.FirstOrDefault(d => d.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)); if (document is not null) document.ProviderId = _adapter.Resolve(file).Info.Id; }
        }
    }
}
