using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WOJD.LocalizationStudio.Models;

public sealed class ProposedTranslationChange : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required LocalizationDocument Document { get; init; }
    public required LocalizationEntry Entry { get; init; }
    public required string Before { get; init; }
    public required string After { get; init; }
    public required string Reason { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string FileName => Document.FileName;
    public string Namespace => Entry.Namespace;
    public string Key => Entry.Key;

    public event PropertyChangedEventHandler? PropertyChanged;
}
