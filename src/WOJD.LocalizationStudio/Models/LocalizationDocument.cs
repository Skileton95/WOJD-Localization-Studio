namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationDocument
{
    public string FilePath { get; init; } = string.Empty;
    public string AdapterId { get; set; } = string.Empty;
    public object? AdapterMetadata { get; set; }

    public DateTime LoadedLastWriteTimeUtc { get; set; }
    public long LoadedFileLength { get; set; }

    public List<LocalizationEntry> Entries { get; } = [];
}
