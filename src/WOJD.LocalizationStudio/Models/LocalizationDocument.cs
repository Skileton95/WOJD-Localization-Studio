using System.Collections.ObjectModel;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationDocument
{
    public string FilePath { get; init; } = string.Empty;
    public ObservableCollection<LocalizationEntry> Entries { get; } = new();
}
