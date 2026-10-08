using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Models;

public sealed class LocalizationDocument
{
    public LocalizationDocument()
    {
        Entries = new LocalizationEntryList(
            entry => EntryRelationIndex.AppendLoadedEntry(this, entry),
            () => EntryRelationIndex.Invalidate(this));

        // Documents are normally populated by NdjsonLocalizationAdapter on a
        // background Task. Initializing the empty index here means each parsed row
        // can extend it incrementally instead of forcing a 600k-row scan on the UI
        // thread when the first row is selected.
        EntryRelationIndex.Initialize(this);
    }

    public string FilePath { get; init; } = string.Empty;
    public LocalizationEntryList Entries { get; }
}
