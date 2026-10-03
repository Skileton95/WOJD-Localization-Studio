using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    private AnnotationStore? _annotations;
    public AnnotationStore Annotations => _annotations ??= new();
    private bool _notesOnly;
    public bool NotesOnly { get => _notesOnly; set { if (SetProperty(ref _notesOnly, value)) { OnPropertyChanged(nameof(ActiveFilterChips)); EntriesView.Refresh(); ScheduleWorkspaceSave(); } } }
    public RowAnnotation? GetAnnotation(LocalizationEntry entry) => _entrySessions.TryGetValue(entry, out var session) ? Annotations.Get(session.Document.FilePath, entry) : null;
    public void SetAnnotation(LocalizationEntry entry, string note, string status)
    {
        if (!_entrySessions.TryGetValue(entry, out var session)) return;
        Annotations.Set(session.Document.FilePath, entry, note, status);
        if (NotesOnly) EntriesView.Refresh();
    }
}