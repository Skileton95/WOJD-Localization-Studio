using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationEntry : ObservableObject
{
    private static readonly IReadOnlySet<TranslationIssueKind> NoValidationKinds =
        new HashSet<TranslationIssueKind>();

    private string _translation = string.Empty;
    private string _savedTranslation = string.Empty;
    private int _validationIssueCount;
    private string _validationSummary = string.Empty;
    private IReadOnlySet<TranslationIssueKind> _validationKinds = NoValidationKinds;
    private string _validatedTranslation = string.Empty;
    private int _validatedConfigurationVersion = -1;
    private bool _validationInitialized;

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
            value ??= string.Empty;
            if (string.Equals(_translation, value, StringComparison.Ordinal))
                return;

            _translation = value;

            // Validate exactly once, before Translation is announced. MainViewModel
            // can then consume the current QA state without re-running every regex,
            // glossary rule and QA profile a second time for the same edit.
            RefreshValidation();

            OnPropertyChanged(nameof(Translation));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CharacterCount));
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
        => RefreshValidationCore(notifyChanges: true);

    private void RefreshValidationCore(bool notifyChanges)
    {
        var configurationVersion = TranslationValidator.ConfigurationVersion;
        if (_validationInitialized &&
            _validatedConfigurationVersion == configurationVersion &&
            string.Equals(_validatedTranslation, Translation, StringComparison.Ordinal))
        {
            return;
        }

        var result = TranslationValidator.Validate(
            Namespace,
            Key,
            Original,
            Translation);

        _validatedTranslation = Translation;
        _validatedConfigurationVersion = configurationVersion;
        _validationInitialized = true;

        if (!notifyChanges)
        {
            _validationIssueCount = result.IssueCount;
            _validationSummary = result.Summary;
            _validationKinds = result.Kinds;
            return;
        }

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

    public void InvalidateValidation()
    {
        _validationInitialized = false;
    }

    public void InitializeSavedTranslation(string value)
    {
        _translation = value ?? string.Empty;
        _savedTranslation = _translation;
        _validationInitialized = false;

        // This method is used while the adapter is constructing a document, before
        // UI listeners exist. Avoid ~10 PropertyChanged notifications per row — at
        // 600k rows those notifications were pure overhead during file opening.
        RefreshValidationCore(notifyChanges: false);
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
