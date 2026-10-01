using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationEntry : INotifyPropertyChanged
{
    private string _translation = string.Empty;
    private bool _isModified;
    private string _validationSummary = string.Empty;

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
        _isModified = false;
        OnPropertyChanged(nameof(Translation));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Status));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
