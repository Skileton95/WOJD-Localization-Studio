using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record ValidationRefreshProgress(
    int Processed,
    int Total,
    int Errors);

public static class ValidationRefreshService
{
    public static async Task RefreshAsync(
        LocalizationDocument document,
        IProgress<ValidationRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int chunkSize = 1000)
    {
        chunkSize = Math.Clamp(chunkSize, 100, 10000);
        var total = document.Entries.Count;
        var errors = 0;

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = document.Entries[i];
            entry.RefreshValidation();

            if (entry.HasValidationIssues)
                errors++;

            var processed = i + 1;
            if (processed == total || processed % chunkSize == 0)
            {
                progress?.Report(
                    new ValidationRefreshProgress(
                        processed,
                        total,
                        errors));

                // QA changes must remain on the UI thread because entries are bound
                // to the DataGrid, but yielding between chunks keeps the window responsive.
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
    }
}
