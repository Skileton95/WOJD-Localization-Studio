namespace WOJD.LocalizationStudio.Models;

internal sealed class DocumentSession
{
    public required LocalizationDocument Document { get; init; }
    public required FileNode Node { get; init; }

    public Dictionary<LocalizationEntry, TranslationStatus> KnownStatuses { get; } = new();
    public Dictionary<LocalizationEntry, string> KnownTranslations { get; } = new();
    public Dictionary<LocalizationEntry, bool> KnownValidationStates { get; } = new();

    public Dictionary<(LocalizationEntry, EntryField), string> KnownFields { get; } = new();

    public Stack<TranslationEdit> UndoStack { get; } = new();
    public Stack<TranslationEdit> RedoStack { get; } = new();

    public LocalizationEntry? SelectedEntry { get; set; }
    public bool HistoryChangeInProgress { get; set; }

    public int TranslatedCount { get; set; }
    public int UntranslatedCount { get; set; }
    public int ModifiedCount { get; set; }
    public int ValidationErrorCount { get; set; }

    public bool HasUnsavedChanges => ModifiedCount > 0;
}

internal sealed record TranslationEdit(
    LocalizationEntry Entry,
    string Before,
    string After,
    EntryField Field = EntryField.Translation,
    IReadOnlyList<TranslationEdit>? Batch = null);
