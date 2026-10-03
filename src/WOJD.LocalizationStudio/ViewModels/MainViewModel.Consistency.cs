using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    private HashSet<string> _inconsistentSources = new(StringComparer.Ordinal);
    public void RefreshConsistency()
    {
        _inconsistentSources = ConsistencyService.Analyze(OpenDocuments).Conflicts.Select(x => x.Text).ToHashSet(StringComparer.Ordinal);
        if (StatusFilter == "Несогласованные") EntriesView.Refresh();
    }
}
