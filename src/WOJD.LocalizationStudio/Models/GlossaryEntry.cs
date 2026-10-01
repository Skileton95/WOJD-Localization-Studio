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

    [JsonIgnore]
    public string LockText => IsLocked ? "Закреплён" : "Подсказка";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
