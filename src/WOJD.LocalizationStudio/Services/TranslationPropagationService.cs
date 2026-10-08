using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record TranslationPropagationPlan(
    IReadOnlyList<BulkTextChange> Changes,
    int ExaminedRows);

public static class TranslationPropagationService
{
    public static TranslationPropagationPlan BuildSameOriginalPlan(
        EntryRelationIndex relations,
        LocalizationEntry source)
    {
        if (string.IsNullOrWhiteSpace(source.Original))
            return new TranslationPropagationPlan([], 0);

        var bucket = relations.GetSameOriginal(source);
        if (bucket.Count == 0)
            return new TranslationPropagationPlan([], 0);

        var changes = new List<BulkTextChange>();
        foreach (var entry in bucket)
        {
            if (ReferenceEquals(entry, source)
                || string.Equals(entry.Translation, source.Translation, StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add(new BulkTextChange(entry, entry.Translation, source.Translation));
        }

        return new TranslationPropagationPlan(changes, bucket.Count);
    }

    public static int Apply(TranslationPropagationPlan plan)
    {
        foreach (var change in plan.Changes)
            change.Entry.Translation = change.After;

        return plan.Changes.Count;
    }
}
