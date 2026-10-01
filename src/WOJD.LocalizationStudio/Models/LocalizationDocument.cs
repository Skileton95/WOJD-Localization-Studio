using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationDocument : INotifyPropertyChanged
{
    private bool _isDirty;

    public string FilePath { get; init; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public ObservableCollection<LocalizationEntry> Entries { get; } = new();

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => IsDirty ? $"{FileName}  •" : FileName;
    public int TotalCount => Entries.Count;
    public int TranslatedCount => Entries.Count(e => !string.IsNullOrWhiteSpace(e.Translation));
    public int ValidationErrorCount => Entries.Count(e => e.HasValidationIssues);
    public string CountText => $"{TotalCount:N0} строк";

    public void RefreshComputedProperties()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TranslatedCount));
        OnPropertyChanged(nameof(ValidationErrorCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(DisplayName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
