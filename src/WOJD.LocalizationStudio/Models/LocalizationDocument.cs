namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationDocument
{
    public string FilePath { get; init; } = string.Empty;
    public LocalizationEntryList Entries { get; } = new();
}
