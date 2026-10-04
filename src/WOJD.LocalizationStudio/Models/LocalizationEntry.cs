using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationEntry : ObservableObject
{
    private string _translation = string.Empty;
    private string _savedTranslation = string.Empty;
    private int _validationIssueCount;
    private string _validationSummary = string.Empty;
    private IReadOnlySet<TranslationIssueKind> _validationKinds =
        new HashSet<TranslationIssueKind>();

    public int Index { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Original { get; init; } = string.Empty;
    public string OriginalDisplay
        => string.IsNullOrEmpty(Original)
            ? "— нет исходного текста —"
            : Original;
    public string TranslationField { get; init; } = "translation";

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
    public IReadOnlySet<TranslationIssueKind> ValidationKinds => _validationKinds;

    public bool HasPlaceholderIssues
        => _validationKinds.Contains(TranslationIssueKind.Placeholder);

    public bool HasTagIssues
        => _validationKinds.Contains(TranslationIssueKind.Tag);

    public bool HasNewLineIssues
        => _validationKinds.Contains(TranslationIssueKind.NewLine);

    public bool HasSameAsSourceIssue
        => _validationKinds.Contains(TranslationIssueKind.SameAsSource);

    public bool HasSuspiciousLengthIssue
        => _validationKinds.Contains(TranslationIssueKind.SuspiciousLength);

    public bool HasSourceMissingIssue
        => _validationKinds.Contains(TranslationIssueKind.SourceMissing);

    public bool HasProfileRuleIssue
        => _validationKinds.Contains(TranslationIssueKind.ProfileRule);

    public bool HasGlossaryIssue
        => _validationKinds.Contains(TranslationIssueKind.Glossary);

    public bool HasStructuralValidationIssues
        => HasPlaceholderIssues || HasTagIssues || HasNewLineIssues;

    public string QaIndicator
        => string.IsNullOrWhiteSpace(Translation)
            ? string.Empty
            : HasSourceMissingIssue
                ? "—"
                : HasStructuralValidationIssues
                    ? "✕"
                    : HasValidationIssues
                        ? "⚠"
                        : "✓";

    public string QaStateText
        => string.IsNullOrWhiteSpace(Translation)
            ? "Нет перевода"
            : HasSourceMissingIssue
                ? "Проверка невозможна"
                : HasStructuralValidationIssues
                    ? "Ошибка"
                    : HasValidationIssues
                        ? "Предупреждение"
                        : "Проверено";

    public void RefreshValidation()
    {
        var result = TranslationValidator.Validate(
            Namespace,
            Key,
            Original,
            Translation);

        var issueCountChanged = SetProperty(
            ref _validationIssueCount,
            result.IssueCount,
            nameof(ValidationIssueCount));

        SetProperty(
            ref _validationSummary,
            result.Summary,
            nameof(ValidationSummary));

        _validationKinds = result.Kinds;
        OnPropertyChanged(nameof(ValidationKinds));
        OnPropertyChanged(nameof(HasPlaceholderIssues));
        OnPropertyChanged(nameof(HasTagIssues));
        OnPropertyChanged(nameof(HasNewLineIssues));
        OnPropertyChanged(nameof(HasSameAsSourceIssue));
        OnPropertyChanged(nameof(HasSuspiciousLengthIssue));
        OnPropertyChanged(nameof(HasSourceMissingIssue));
        OnPropertyChanged(nameof(HasProfileRuleIssue));
        OnPropertyChanged(nameof(HasGlossaryIssue));
        OnPropertyChanged(nameof(HasStructuralValidationIssues));
        OnPropertyChanged(nameof(QaIndicator));
        OnPropertyChanged(nameof(QaStateText));

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
