using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public WojdProject? Project { get; private set; }
    public async Task OpenWojdProjectAsync(string path)
    {
        var project = WojdProjectService.Load(path);
        Project = project;
        foreach (var source in project.Sources.Where(s => s.Role == "RU" && s.Format != "locres"))
        {
            var file = WojdProjectService.Resolve(project, source.Path);
            if (System.IO.File.Exists(file) && new NdjsonLocalizationAdapter().CanOpen(file)) await LoadPathAsync(file);
        }
    }
}
