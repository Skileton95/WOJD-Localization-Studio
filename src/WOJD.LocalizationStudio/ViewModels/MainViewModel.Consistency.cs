using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    private HashSet<string> _inconsistentSources = new(StringComparer.Ordinal);
    public void RefreshConsistency()
    {
        _inconsistentSources = OpenDocuments.SelectMany(x => x.Entries).Where(x => x.Original.Length > 0 && !string.IsNullOrWhiteSpace(x.Translation))
            .GroupBy(x => x.Original, StringComparer.Ordinal).Where(g => g.Select(x => x.Translation).Distinct(StringComparer.Ordinal).Take(2).Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        if (StatusFilter == "Несогласованные") EntriesView.Refresh();
    }
}
