using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public interface ILocalizationFileAdapter
{
    bool CanOpen(string path);
    Task<LocalizationDocument> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null);
    Task SaveAsync(LocalizationDocument document, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null);
}
