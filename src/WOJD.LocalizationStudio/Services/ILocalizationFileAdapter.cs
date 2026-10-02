using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public interface ILocalizationFileAdapter
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyCollection<string> Extensions { get; }

    bool CanOpen(string path);
    Task<LocalizationDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default);
    Task SaveAsync(
        LocalizationDocument document,
        CancellationToken cancellationToken = default);
}
