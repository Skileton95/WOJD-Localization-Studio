using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public IReadOnlyCollection<string> IntentionalConsistency => _consistencyExceptions;
    private bool _consistencyDirty = true;
    private HashSet<WOJD.LocalizationStudio.Models.LocalizationDocument> _consistencyDocuments = [];
    private HashSet<string> _consistencyExceptions = new(StringComparer.Ordinal);
    public bool ToggleConsistencyException(string source) { var added = _consistencyExceptions.Add(source); if (!added) _consistencyExceptions.Remove(source); _consistencyDirty = true; RefreshConsistency(); ScheduleWorkspaceSave(); return added; }
    private HashSet<string> _inconsistentSources = new(StringComparer.Ordinal);
    public void RefreshConsistency()
    {
        var documents = OpenDocuments.ToHashSet();
        if (!_consistencyDirty && _consistencyDocuments.SetEquals(documents)) return;
        _consistencyDocuments = documents; _consistencyDirty = false;
        _inconsistentSources = OpenDocuments.SelectMany(x => x.Entries).Where(x => x.Original.Length > 0 && !string.IsNullOrWhiteSpace(x.Translation))
            .GroupBy(x => x.Original, StringComparer.Ordinal).Where(g => g.Select(x => x.Translation).Distinct(StringComparer.Ordinal).Take(2).Count() > 1)
            .Select(g => g.Key).Where(x => !_consistencyExceptions.Contains(x)).ToHashSet(StringComparer.Ordinal);
        if (StatusFilter == "Несогласованные") EntriesView.Refresh();
    }
}
