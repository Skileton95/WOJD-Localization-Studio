using System.IO;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record CollectionError(
    string FilePath,
    string Message);

public sealed record ProjectCollectionResult(
    List<LocalizationDocument> Documents,
    List<CollectionError> Errors,
    List<SyncConflict> Conflicts,
    int Files,
    int Entries);

public static class ProjectCollectionService
{
    public static async Task<ProjectCollectionResult> CollectAsync(
        string folder,
        CancellationToken cancellationToken = default)
    {
        var registry =
            new LocalizationAdapterRegistry();

        var documents =
            new List<LocalizationDocument>();

        var errors =
            new List<CollectionError>();

        var files =
            Directory
                .EnumerateFiles(
                    folder,
                    "*",
                    SearchOption.AllDirectories)
                .Where(registry.CanOpen)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                documents.Add(
                    await registry.LoadAsync(
                        file,
                        cancellationToken));
            }
            catch (Exception ex)
            {
                errors.Add(
                    new CollectionError(
                        file,
                        ex.Message));
            }
        }

        var conflicts =
            ProjectSyncService.FindConflicts(
                documents);

        return new ProjectCollectionResult(
            documents,
            errors,
            conflicts,
            files.Count,
            documents.Sum(x => x.Entries.Count));
    }
}
