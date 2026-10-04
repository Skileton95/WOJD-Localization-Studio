using System.Runtime.CompilerServices;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record EntryHistoryItem(
    DateTimeOffset Timestamp,
    string Reason,
    string Before,
    string After);

public static class EntryHistoryService
{
    private const int MaxItemsPerEntry = 100;

    private static readonly ConditionalWeakTable<LocalizationEntry, HistoryBucket>
        Buckets = new();

    private static readonly AsyncLocal<string?> CurrentReasonState = new();

    public static IDisposable BeginOperation(string reason)
    {
        var previous = CurrentReasonState.Value;
        CurrentReasonState.Value = reason;
        return new ReasonScope(previous);
    }

    public static void Record(
        LocalizationEntry entry,
        string before,
        string after,
        string? reason = null)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        var resolvedReason =
            reason
            ?? CurrentReasonState.Value
            ?? "Изменение перевода";

        var bucket = Buckets.GetOrCreateValue(entry);

        lock (bucket.Sync)
        {
            bucket.Items.Add(
                new EntryHistoryItem(
                    DateTimeOffset.Now,
                    resolvedReason,
                    before,
                    after));

            if (bucket.Items.Count > MaxItemsPerEntry)
            {
                bucket.Items.RemoveRange(
                    0,
                    bucket.Items.Count - MaxItemsPerEntry);
            }
        }

        OperationJournalService.Record(
            entry,
            before,
            after,
            resolvedReason);
    }

    public static IReadOnlyList<EntryHistoryItem> GetHistory(
        LocalizationEntry entry)
    {
        if (!Buckets.TryGetValue(entry, out var bucket))
            return [];

        lock (bucket.Sync)
        {
            return bucket.Items
                .OrderByDescending(x => x.Timestamp)
                .ToList();
        }
    }

    private sealed class HistoryBucket
    {
        public object Sync { get; } = new();
        public List<EntryHistoryItem> Items { get; } = [];
    }

    private sealed class ReasonScope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CurrentReasonState.Value = previous;
        }
    }
}
