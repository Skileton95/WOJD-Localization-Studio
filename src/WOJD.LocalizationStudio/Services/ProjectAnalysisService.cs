using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record NamespaceStat(
    string Namespace,
    int Total,
    int Translated,
    int Untranslated,
    int Errors,
    double ProgressPercent,
    bool Favorite = false);

public sealed record ConsistencyIssue(
    string Kind,
    string Source,
    IReadOnlyList<string> Variants,
    int Occurrences,
    IReadOnlyList<LocalizationEntry> Entries);

public sealed record ProjectStatistics(
    int Files,
    int Entries,
    int Translated,
    int Untranslated,
    int Modified,
    int QaErrors,
    int UniqueSources,
    int DuplicateSources,
    int Namespaces);

public sealed record ReviewQueueItem(
    string FilePath,
    int Index,
    string Namespace,
    string Key,
    string Reason,
    LocalizationEntry Entry);

public sealed record ContextEntry(
    string FilePath,
    int Index,
    string Namespace,
    string Key,
    string Original,
    string Translation,
    string Relation,
    LocalizationEntry Entry);

public static partial class ProjectAnalysisService
{
    public static List<NamespaceStat> BuildNamespaceStats(
        IEnumerable<LocalizationDocument> documents,
        ISet<string>? favorites = null)
    {
        return documents
            .SelectMany(x => x.Entries)
            .GroupBy(x => x.Namespace)
            .Select(group =>
            {
                var list = group.ToList();
                var total = list.Count;
                var translated =
                    list.Count(x =>
                        !string.IsNullOrWhiteSpace(x.Translation));
                var untranslated =
                    total - translated;
                var errors =
                    list.Count(x => x.HasValidationIssues);

                return new NamespaceStat(
                    group.Key,
                    total,
                    translated,
                    untranslated,
                    errors,
                    total == 0
                        ? 0
                        : translated * 100.0 / total,
                    favorites?.Contains(group.Key) == true);
            })
            .OrderByDescending(x => x.Favorite)
            .ThenBy(x => x.Namespace, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<ConsistencyIssue> FindConsistencyIssues(
        IEnumerable<LocalizationDocument> documents)
    {
        var entries =
            documents
                .SelectMany(x => x.Entries)
                .ToList();

        var issues =
            new List<ConsistencyIssue>();

        foreach (var group in
                 entries
                     .Where(x => !string.IsNullOrWhiteSpace(x.Original))
                     .GroupBy(x => x.Original))
        {
            var translations =
                group
                    .Select(x => x.Translation.Trim())
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

            if (translations.Count > 1)
            {
                issues.Add(
                    new ConsistencyIssue(
                        "Один оригинал — разные переводы",
                        group.Key,
                        translations,
                        group.Count(),
                        group.ToList()));
            }
        }

        foreach (var group in
                 entries
                     .Where(x => !string.IsNullOrWhiteSpace(x.Translation))
                     .GroupBy(x => x.Translation.Trim()))
        {
            var originals =
                group
                    .Select(x => x.Original)
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

            if (originals.Count > 1)
            {
                issues.Add(
                    new ConsistencyIssue(
                        "Один перевод — разные оригиналы",
                        group.Key,
                        originals,
                        group.Count(),
                        group.ToList()));
            }
        }

        return issues
            .OrderByDescending(x => x.Occurrences)
            .ThenBy(x => x.Source, StringComparer.Ordinal)
            .ToList();
    }

    public static ProjectStatistics BuildStatistics(
        IEnumerable<LocalizationDocument> documents)
    {
        var docs = documents.ToList();
        var entries = docs.SelectMany(x => x.Entries).ToList();

        var duplicateSources =
            entries
                .Where(x => !string.IsNullOrWhiteSpace(x.Original))
                .GroupBy(x => x.Original)
                .Count(x => x.Count() > 1);

        return new ProjectStatistics(
            docs.Count,
            entries.Count,
            entries.Count(x =>
                !string.IsNullOrWhiteSpace(x.Translation)),
            entries.Count(x =>
                string.IsNullOrWhiteSpace(x.Translation)),
            entries.Count(x =>
                x.Status == TranslationStatus.Modified),
            entries.Count(x =>
                x.HasValidationIssues),
            entries
                .Select(x => x.Original)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            duplicateSources,
            entries
                .Select(x => x.Namespace)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    public static List<ReviewQueueItem> BuildReviewQueue(
        IEnumerable<LocalizationDocument> documents,
        StudioProjectMetadata metadata)
    {
        var result =
            new List<ReviewQueueItem>();

        foreach (var document in documents)
        {
            foreach (var entry in document.Entries)
            {
                string? reason = null;

                if (string.IsNullOrWhiteSpace(entry.Translation))
                    reason = "Без перевода";
                else if (entry.HasValidationIssues)
                    reason = $"QA: {entry.ValidationSummary}";
                else if (ContainsCjk(entry.Translation))
                    reason = "В переводе остались CJK-символы";
                else if (HasGlossaryViolation(
                             entry,
                             metadata.Glossary))
                    reason = "Нарушение глоссария";

                var noteId =
                    StudioProjectMetadataService.EntryId(
                        document.FilePath,
                        entry.Namespace,
                        entry.Key);

                if (metadata.Notes.TryGetValue(
                        noteId,
                        out var note) &&
                    string.Equals(
                        note.State,
                        "Нужно проверить",
                        StringComparison.OrdinalIgnoreCase))
                {
                    reason =
                        reason is null
                            ? "Помечено: нужно проверить"
                            : reason + " • Нужно проверить";
                }

                if (reason is not null)
                {
                    result.Add(
                        new ReviewQueueItem(
                            document.FilePath,
                            entry.Index,
                            entry.Namespace,
                            entry.Key,
                            reason,
                            entry));
                }
            }
        }

        return result;
    }

    public static List<ContextEntry> BuildContext(
        LocalizationEntry selected,
        string selectedFile,
        IEnumerable<LocalizationDocument> documents)
    {
        var result =
            new List<ContextEntry>();

        foreach (var document in documents)
        {
            foreach (var entry in document.Entries)
            {
                if (ReferenceEquals(entry, selected))
                    continue;

                string? relation = null;

                if (string.Equals(
                        entry.Namespace,
                        selected.Namespace,
                        StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(entry.Index - selected.Index) <= 3)
                {
                    relation = "Соседняя строка";
                }
                else if (!string.IsNullOrWhiteSpace(selected.Original) &&
                         string.Equals(
                             entry.Original,
                             selected.Original,
                             StringComparison.Ordinal))
                {
                    relation = "Тот же оригинал";
                }
                else if (!string.IsNullOrWhiteSpace(selected.Translation) &&
                         string.Equals(
                             entry.Translation,
                             selected.Translation,
                             StringComparison.Ordinal))
                {
                    relation = "Тот же перевод";
                }

                if (relation is null)
                    continue;

                result.Add(
                    new ContextEntry(
                        document.FilePath,
                        entry.Index,
                        entry.Namespace,
                        entry.Key,
                        entry.Original,
                        entry.Translation,
                        relation,
                        entry));
            }
        }

        return result
            .OrderBy(x => x.Relation)
            .ThenBy(x => x.FilePath)
            .ThenBy(x => x.Index)
            .Take(200)
            .ToList();
    }

    public static bool MatchesSmartFilter(
        LocalizationEntry entry,
        SavedSmartFilter filter)
    {
        if (filter.Untranslated &&
            !string.IsNullOrWhiteSpace(entry.Translation))
        {
            return false;
        }

        if (filter.QaErrors &&
            !entry.HasValidationIssues)
        {
            return false;
        }

        if (filter.EmptySource &&
            !string.IsNullOrWhiteSpace(entry.Original))
        {
            return false;
        }

        if (filter.ContainsCjk &&
            !ContainsCjk(entry.Translation))
        {
            return false;
        }

        if (filter.HasPlaceholders &&
            !PlaceholderRegex().IsMatch(entry.Original))
        {
            return false;
        }

        if (filter.LongTranslation &&
            entry.Original.Length > 0 &&
            entry.Translation.Length <=
                Math.Max(
                    entry.Original.Length * 1.8,
                    entry.Original.Length + 30))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Namespace) &&
            !string.Equals(
                entry.Namespace,
                filter.Namespace,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static bool ContainsCjk(string text)
        => text.Any(ch =>
            ch is >= '\u3400' and <= '\u4DBF' ||
            ch is >= '\u4E00' and <= '\u9FFF' ||
            ch is >= '\uF900' and <= '\uFAFF');

    public static bool HasGlossaryViolation(
        LocalizationEntry entry,
        IEnumerable<GlossaryTerm> glossary)
    {
        foreach (var term in glossary)
        {
            if (string.IsNullOrWhiteSpace(term.Source) ||
                string.IsNullOrWhiteSpace(term.Translation))
            {
                continue;
            }

            if (!entry.Original.Contains(
                    term.Source,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(
                    term.Requirement,
                    "Запрещено",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (entry.Translation.Contains(
                        term.Translation,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (!entry.Translation.Contains(
                    term.Translation,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\{[^{}]+\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[a-zA-Z]")]
    private static partial Regex PlaceholderRegex();
}
