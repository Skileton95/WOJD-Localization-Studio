using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationEntry : ObservableObject
{
    private string _translation = string.Empty;
    private string _savedTranslation = string.Empty;
    private int _validationIssueCount;
    private string _validationSummary = string.Empty;

    public int Index { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    private string _original = string.Empty;

    public string Original
    {
        get => _original;
        init => _original = value;
    }

    public string OriginalDisplay
        => string.IsNullOrEmpty(Original)
            ? "— нет исходного текста —"
            : Original;

    public void SetOriginalContext(string value)
    {
        if (string.Equals(
                _original,
                value,
                StringComparison.Ordinal))
        {
            return;
        }

        _original = value ?? string.Empty;
        OnPropertyChanged(nameof(Original));
        OnPropertyChanged(nameof(OriginalDisplay));
        RefreshValidation();
    }
    public string TranslationField { get; init; } = "translation";
    public object? AdapterMetadata { get; set; }
    public string Identity => $"{Namespace}:{Key}";

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
        => Translation != _savedTranslation
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
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
    }
}
