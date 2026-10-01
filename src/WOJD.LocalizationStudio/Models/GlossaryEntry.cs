using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WOJD.LocalizationStudio.Models;

public sealed class GlossaryEntry : INotifyPropertyChanged
{
    private string _source = string.Empty;
    private string _translation = string.Empty;
    private string _note = string.Empty;
    private bool _isLocked = true;
    private List<string> _allowedTranslations = [];
    private List<string> _namespaceScopes = [];
    private int _priority;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Source
    {
        get => _source;
        set
        {
            value ??= string.Empty;
            if (_source == value) return;
            _source = value;
            OnPropertyChanged();
        }
    }

    public string Translation
    {
        get => _translation;
        set
        {
            value ??= string.Empty;
            if (_translation == value) return;
            _translation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllowedText));
        }
    }

    public string Note
    {
        get => _note;
        set
        {
            value ??= string.Empty;
            if (_note == value) return;
            _note = value;
            OnPropertyChanged();
        }
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value) return;
            _isLocked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LockText));
        }
    }

    public List<string> AllowedTranslations
    {
        get => _allowedTranslations;
        set
        {
            _allowedTranslations = NormalizeList(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllowedText));
        }
    }

    public List<string> NamespaceScopes
    {
        get => _namespaceScopes;
        set
        {
            _namespaceScopes = NormalizeList(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScopeText));
        }
    }

    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority == value) return;
            _priority = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string LockText => IsLocked ? "Закреплён" : "Подсказка";

    [JsonIgnore]
    public string ScopeText => NamespaceScopes.Count == 0
        ? "Везде"
        : string.Join(", ", NamespaceScopes);

    [JsonIgnore]
    public string AllowedText
    {
        get
        {
            var variants = GetAcceptedTranslations();
            return variants.Count <= 1
                ? "—"
                : string.Join(" | ", variants.Skip(1));
        }
    }

    public IReadOnlyList<string> GetAcceptedTranslations()
    {
        var result = new List<string>();

        if (!string.IsNullOrWhiteSpace(Translation))
            result.Add(Translation.Trim());

        foreach (var value in AllowedTranslations)
        {
            if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
                result.Add(value);
        }

        return result;
    }

    public bool AppliesToNamespace(string? entryNamespace)
    {
        if (NamespaceScopes.Count == 0)
            return true;

        return NamespaceScopes.Any(scope =>
            string.Equals(scope, entryNamespace ?? string.Empty, StringComparison.Ordinal));
    }

    public bool IsTranslationAccepted(string? translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return false;

        return GetAcceptedTranslations().Any(variant =>
            !string.IsNullOrWhiteSpace(variant) &&
            translation.Contains(variant, StringComparison.OrdinalIgnoreCase));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static List<string> NormalizeList(IEnumerable<string>? values) =>
        values?
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? [];

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
