using System.Collections.ObjectModel;
using WOJD.LocalizationStudio.Infrastructure;

namespace WOJD.LocalizationStudio.Models;

public sealed class FileNode : ObservableObject
{
    private int _entryCount;
    private bool _isModified;

    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }

    public string DisplayName
        => IsModified ? $"{Name} *" : Name;

    public int EntryCount
    {
        get => _entryCount;
        set => SetProperty(ref _entryCount, value);
    }

    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (SetProperty(ref _isModified, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public ObservableCollection<FileNode> Children { get; } = new();
}
