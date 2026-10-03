using System.IO;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    private string? _glossaryPath;
    private List<GlossaryTerm> _glossary = [];
    public IReadOnlyList<GlossaryTerm> Glossary
    {
        get
        {
            var path = Path.Combine(ProjectDataDirectory, "glossary.json");
            if (_glossaryPath != path) { _glossary = GlossaryService.Load(path); _glossaryPath = path; }
            return _glossary;
        }
    }
    public string GlossaryHints
    {
        get { try { return SelectedEntry is null ? "" : GlossaryService.Hints(Glossary, SelectedEntry); } catch (Exception e) { IssueLogService.Record(e.Message); return "Глоссарий недоступен: " + e.Message; } }
    }
    public void ReloadGlossary() { _glossaryPath = null; OnPropertyChanged(nameof(GlossaryHints)); }
}
