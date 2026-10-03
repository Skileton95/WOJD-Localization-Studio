using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationEntry : ObservableObject
{
    private string _translation = string.Empty;
    private string _savedTranslation = string.Empty;
    private int _validationIssueCount;
    private string _validationSummary = string.Empty;

    public int LineNumber { get; init; }
    public int Index { get; init; }
    private string _namespace = "", _key = "", _original = "";
    private string _savedNamespace = "", _savedKey = "", _savedOriginal = "";
    public string Namespace { get => _namespace; set { if (SetProperty(ref _namespace, value)) NotifyIdentity(); } }
    public string Key { get => _key; set { if (SetProperty(ref _key, value)) NotifyIdentity(); } }
    public string Original { get => _original; set { if (SetProperty(ref _original, value)) { NotifyIdentity(); OnPropertyChanged(nameof(OriginalDisplay)); RefreshValidation(); } } }
    public string SavedNamespace => _savedNamespace;
    public string SavedKey => _savedKey;
    public string SavedOriginal => _savedOriginal;
    public string SavedTranslation => _savedTranslation;
    public bool NamespaceModified => Namespace != _savedNamespace;
    public bool KeyModified => Key != _savedKey;
    public bool OriginalModified => Original != _savedOriginal;
    private void NotifyIdentity() { OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); }
    public string GetField(EntryField field) => field switch { EntryField.Namespace => Namespace, EntryField.Key => Key, EntryField.Original => Original, _ => Translation };
    public void SetField(EntryField field, string value)
    {
        switch (field) { case EntryField.Namespace: Namespace = value; break; case EntryField.Key: Key = value; break; case EntryField.Original: Original = value; break; default: Translation = value; break; }
    }
    public string OriginalDisplay
        => string.IsNullOrEmpty(Original)
            ? "— нет исходного текста —"
            : Original;
    public string TranslationField { get; init; } = "translation";

    // Исходная NDJSON-строка хранится как текст.
    // Это значительно дешевле по памяти, чем держать JsonObject для каждой записи.
    public string RawLine { get; set; } = string.Empty;

    public string Translation
    {
        get => _translation;
        set
        {
            if (SetProperty(ref _translation, value))
            {
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CharacterCount));
                RefreshValidation();
            }
        }
    }

    public TranslationStatus Status
        => Translation != _savedTranslation || NamespaceModified || KeyModified || OriginalModified
            ? TranslationStatus.Modified
            : string.IsNullOrWhiteSpace(Translation)
                ? TranslationStatus.Untranslated
                : TranslationStatus.Translated;

    public string StatusText => Status switch
    {
        TranslationStatus.Modified => "Изменено",
        TranslationStatus.Untranslated => "Без перевода",
        _ => "Переведено"
    };

    public int CharacterCount => Translation.Length;

    public int ValidationIssueCount => _validationIssueCount;
    public bool HasValidationIssues => _validationIssueCount > 0;
    public string ValidationSummary => _validationSummary;

    public void RefreshValidation()
    {
        var result =
            TranslationValidator.Validate(
                Original,
                Translation);

        var issueCountChanged =
            SetProperty(
                ref _validationIssueCount,
                result.IssueCount,
                nameof(ValidationIssueCount));

        SetProperty(
            ref _validationSummary,
            result.Summary,
            nameof(ValidationSummary));

        if (issueCountChanged)
            OnPropertyChanged(nameof(HasValidationIssues));
    }

    public void InitializeSavedTranslation(string value)
    {
        _translation = value;
        _savedTranslation = value;
        _savedNamespace = Namespace; _savedKey = Key; _savedOriginal = Original;

        OnPropertyChanged(nameof(Translation));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CharacterCount));
        RefreshValidation();
    }

    public void MarkSaved(string? rawLine = null)
    {
        if (rawLine is not null)
            RawLine = rawLine;

        _savedTranslation = Translation;
        _savedNamespace = Namespace; _savedKey = Key; _savedOriginal = Original;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
    }
}
