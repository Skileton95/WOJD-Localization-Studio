namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationDocument
{
    public string FilePath { get; init; } = string.Empty;
    public string? DiskHash { get; set; }
    public List<LocalizationEntry> Entries { get; } = [];
}
