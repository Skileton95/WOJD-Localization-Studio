using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationEntry : INotifyPropertyChanged
{
    private string _translation = string.Empty;
    private bool _isModified;
    private string _validationSummary = string.Empty;
    private ValidationIssueKind _validationKinds;
    private bool _hasCriticalValidationIssues;
    private string _lastSavedTranslation = string.Empty;

    public int LineNumber { get; set; }
    public string Namespace { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;

    public string Translation
    {
        get => _translation;
        set
        {
            value ??= string.Empty;
            if (_translation == value) return;
            _translation = value;
            IsModified = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
        }
    }

    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value) return;
            _isModified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
        }
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        set
        {
            value ??= string.Empty;
            if (_validationSummary == value) return;
            _validationSummary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationIssues));
            OnPropertyChanged(nameof(Status));
        }
    }

    public bool HasValidationIssues => !string.IsNullOrWhiteSpace(ValidationSummary);

    public ValidationIssueKind ValidationKinds
    {
        get => _validationKinds;
        set
        {
            if (_validationKinds == value) return;
            _validationKinds = value;
            OnPropertyChanged();
        }
    }

    public bool HasCriticalValidationIssues
    {
        get => _hasCriticalValidationIssues;
        set
        {
            if (_hasCriticalValidationIssues == value) return;
            _hasCriticalValidationIssues = value;
            OnPropertyChanged();
        }
    }

    public string LastSavedTranslation
    {
        get => _lastSavedTranslation;
        private set => _lastSavedTranslation = value ?? string.Empty;
    }

    public JsonObject Raw { get; set; } = new();
    public string TranslationPropertyName { get; set; } = "translated";

    public string Status => HasValidationIssues
        ? "Ошибка"
        : string.IsNullOrWhiteSpace(Translation)
            ? "Не переведено"
            : IsModified ? "Изменено" : "Готово";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLoadedTranslation(string value)
    {
        _translation = value ?? string.Empty;
        _lastSavedTranslation = _translation;
        _isModified = false;
        OnPropertyChanged(nameof(Translation));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Status));
    }

    public void MarkSaved()
    {
        LastSavedTranslation = Translation;
        IsModified = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
