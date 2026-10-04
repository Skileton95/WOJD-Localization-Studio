using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record AiBatchEstimate(
    int Entries,
    long ApproxInputTokens,
    long ApproxOutputTokens,
    decimal? EstimatedUsd)
{
    public string CostText
        => EstimatedUsd is null
            ? "стоимость не рассчитана — тариф модели не задан в настройках"
            : $"≈ ${EstimatedUsd.Value:0.####}";
}

public sealed record AiBatchItemResult(
    LocalizationEntry Entry,
    AiReviewResult? Review,
    string? Error)
{
    public bool Success => Review is not null && string.IsNullOrWhiteSpace(Error);
}

public static class AiBatchReviewService
{
    public static AiBatchEstimate Estimate(
        IReadOnlyCollection<LocalizationEntry> entries,
        EditorSettings settings)
    {
        long inputChars = 0;
        long outputChars = 0;

        foreach (var entry in entries)
        {
            inputChars += entry.Original?.Length ?? 0;
            inputChars += entry.Translation?.Length ?? 0;
            inputChars += entry.Namespace.Length + entry.Key.Length + 240;
            outputChars += Math.Max(80, entry.Translation?.Length ?? 0) + 120;
        }

        var inputTokens = Math.Max(1, inputChars / 3);
        var outputTokens = Math.Max(1, outputChars / 3);
        decimal? cost = null;

        if (settings.AiInputUsdPerMillionTokens > 0 ||
            settings.AiOutputUsdPerMillionTokens > 0)
        {
            cost =
                inputTokens / 1_000_000m * settings.AiInputUsdPerMillionTokens +
                outputTokens / 1_000_000m * settings.AiOutputUsdPerMillionTokens;
        }

        return new AiBatchEstimate(
            entries.Count,
            inputTokens,
            outputTokens,
            cost);
    }

    public static async Task<IReadOnlyList<AiBatchItemResult>> ReviewAsync(
        IReadOnlyList<LocalizationEntry> entries,
        EditorSettings settings,
        IProgress<BackgroundOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Min(entries.Count, settings.AiBatchLimit);
        var selected = entries.Take(limit).ToList();
        var results = new AiBatchItemResult?[selected.Count];
        var completed = 0;
        using var gate = new SemaphoreSlim(settings.AiConcurrency, settings.AiConcurrency);

        var tasks = selected.Select((entry, index) => Task.Run(async () =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                results[index] = await ReviewWithRetryAsync(
                    entry,
                    settings.AiRetryCount,
                    cancellationToken);
            }
            finally
            {
                gate.Release();
                var current = Interlocked.Increment(ref completed);
                progress?.Report(new BackgroundOperationProgress(
                    current,
                    selected.Count,
                    "ИИ-проверка"));
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);
        return results.Where(x => x is not null).Cast<AiBatchItemResult>().ToList();
    }

    private static async Task<AiBatchItemResult> ReviewWithRetryAsync(
        LocalizationEntry entry,
        int retryCount,
        CancellationToken cancellationToken)
    {
        Exception? last = null;

        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await AiCorrectionService.ReviewAsync(entry, cancellationToken);
                return new AiBatchItemResult(entry, result, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt >= retryCount)
                    break;

                await Task.Delay(
                    TimeSpan.FromMilliseconds(650 * Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        return new AiBatchItemResult(
            entry,
            null,
            last?.Message ?? "Неизвестная ошибка ИИ-проверки.");
    }
}
