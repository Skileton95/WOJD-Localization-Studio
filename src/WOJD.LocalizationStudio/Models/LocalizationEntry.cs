using System.Text.Json.Nodes;
using WOJD.LocalizationStudio.Infrastructure;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationEntry : ObservableObject
{
    private string _translation = string.Empty;
    private string _savedTranslation = string.Empty;

    public int Index { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Original { get; init; } = string.Empty;
    public string TranslationField { get; init; } = "translation";
    public JsonObject RawObject { get; init; } = new();

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

    public void InitializeSavedTranslation(string value)
    {
        _translation = value;
        _savedTranslation = value;
        OnPropertyChanged(nameof(Translation));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CharacterCount));
    }

    public void MarkSaved()
    {
        _savedTranslation = Translation;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
    }
}
