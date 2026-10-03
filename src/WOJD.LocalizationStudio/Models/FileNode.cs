using System.Collections.ObjectModel;
using WOJD.LocalizationStudio.Infrastructure;

namespace WOJD.LocalizationStudio.Models;

public sealed class FileNode : ObservableObject
{
    private int _entryCount;
    private bool _isModified;
    private bool _isActive;
    private bool _isPinned;

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }

    public string DisplayName
        => $"{(IsActive ? "● " : string.Empty)}{Name}{(IsModified ? " *" : string.Empty)}";

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

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public ObservableCollection<FileNode> Children { get; } = new();
}
