using System.Collections.ObjectModel;

namespace WOJD.LocalizationStudio.Models;

public sealed class FileNode
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public int EntryCount { get; set; }
    public ObservableCollection<FileNode> Children { get; } = new();
}
