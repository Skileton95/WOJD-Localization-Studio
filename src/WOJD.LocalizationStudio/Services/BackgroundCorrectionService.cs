using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record BackgroundOperationProgress(
    int Current,
    int Total,
    string Stage)
{
    public int Percent => Total <= 0 ? 0 : (int)Math.Clamp((long)Current * 100 / Total, 0, 100);
}

public static class BackgroundCorrectionService
{
    public static Task<TranslationCorrectionPlan> BuildPlanAsync(
        IReadOnlyList<LocalizationEntry> entries,
        ITranslationCorrectionProvider? provider = null,
        IProgress<BackgroundOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        provider ??= TranslationAutoCorrectionService.RuleBased;
        var selectedProvider = provider;

        return Task.Run(
            () => BuildPlan(entries, selectedProvider, progress, cancellationToken),
            cancellationToken);
    }

    private static TranslationCorrectionPlan BuildPlan(
        IReadOnlyList<LocalizationEntry> entries,
        ITranslationCorrectionProvider provider,
        IProgress<BackgroundOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var changes = new List<TranslationCorrectionChange>();
        var reviewItems = new List<TranslationCorrectionReviewItem>();
        var total = entries.Count;

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];

            if (!string.IsNullOrWhiteSpace(entry.Translation))
            {
                var before = entry.Translation;
                var result = provider.Correct(entry);

                if (result.RequiresReview)
                {
                    reviewItems.Add(new TranslationCorrectionReviewItem(
                        entry,
                        result.ReviewReason));
                }

                if (result.HasChanges &&
                    !string.Equals(before, result.CorrectedText, StringComparison.Ordinal))
                {
                    changes.Add(new TranslationCorrectionChange(
                        entry,
                        before,
                        result.CorrectedText,
                        result.FixCount,
                        result.AppliedRules));
                }
            }

            if (i % 1000 == 0 || i + 1 == total)
            {
                progress?.Report(new BackgroundOperationProgress(
                    i + 1,
                    total,
                    "Анализ автоисправлений"));
            }
        }

        return new TranslationCorrectionPlan(changes, reviewItems);
    }
}
