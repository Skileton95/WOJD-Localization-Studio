using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record TranslationCorrectionProgress(
    int Processed,
    int Total,
    int Changes,
    int ReviewItems);

public static class TranslationAutoCorrectionProgressService
{
    public static Task<TranslationCorrectionPlan> BuildPlanAsync(
        IEnumerable<LocalizationEntry> entries,
        IProgress<TranslationCorrectionProgress>? progress = null,
        ITranslationCorrectionProvider? provider = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => BuildPlan(entries, progress, provider, cancellationToken),
            cancellationToken);

    public static TranslationCorrectionPlan BuildPlan(
        IEnumerable<LocalizationEntry> entries,
        IProgress<TranslationCorrectionProgress>? progress = null,
        ITranslationCorrectionProvider? provider = null,
        CancellationToken cancellationToken = default)
    {
        provider ??= TranslationAutoCorrectionService.RuleBased;

        if (!provider.IsAvailable)
            return new TranslationCorrectionPlan([]);

        var candidates = entries.Distinct().ToList();
        var changes = new List<TranslationCorrectionChange>();
        var reviews = new List<TranslationCorrectionReviewItem>();
        var processed = 0;

        foreach (var entry in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(entry.Translation))
            {
                var before = entry.Translation;
                var result = provider.Correct(entry);

                if (result.RequiresReview)
                {
                    reviews.Add(
                        new TranslationCorrectionReviewItem(
                            entry,
                            result.ReviewReason));
                }

                if (result.HasChanges &&
                    !string.Equals(before, result.CorrectedText, StringComparison.Ordinal))
                {
                    changes.Add(
                        new TranslationCorrectionChange(
                            entry,
                            before,
                            result.CorrectedText,
                            result.FixCount,
                            result.AppliedRules));
                }
            }

            processed++;

            if (processed == candidates.Count || processed % 1000 == 0)
            {
                progress?.Report(
                    new TranslationCorrectionProgress(
                        processed,
                        candidates.Count,
                        changes.Count,
                        reviews.Count));
            }
        }

        return new TranslationCorrectionPlan(changes, reviews);
    }
}
