using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public SmartFilter SmartFilters { get; private set; } = new();
    public List<SavedFilter> SavedFilters { get; private set; } = [];
    public IReadOnlyList<FilterChip> ActiveFilterChips => SmartFilters.Chips.Concat(NotesOnly ? new[] { new FilterChip("Notes", "Заметки") } : []).ToList();
    public void SetSmartFilters(SmartFilter filter)
    {
        SmartFilters = filter; OnPropertyChanged(nameof(ActiveFilterChips)); EntriesView.Refresh(); ScheduleWorkspaceSave();
    }
    public void SaveFilter(string name, SmartFilter filter)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Введите имя фильтра.");
        SavedFilters.RemoveAll(x => x.Name == name); SavedFilters.Add(new(name, filter)); ScheduleWorkspaceSave();
    }
    public void ResetAllFilters() { NotesOnly = false; SearchText = ""; NamespaceFilter = null; StatusFilter = "Все"; SetSmartFilters(new()); }
}