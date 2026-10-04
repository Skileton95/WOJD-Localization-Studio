using System.Text.RegularExpressions;

namespace WOJD.LocalizationStudio.Services;

public sealed record StructuralQaResult(
    bool HasIssues,
    string Summary,
    IReadOnlyList<string> MissingTags,
    IReadOnlyList<string> ExtraTags,
    IReadOnlyList<string> MissingPlaceholders,
    IReadOnlyList<string> ExtraPlaceholders,
    bool TagOrderMismatch);

public static class StructuralQaService
{
    private static readonly Regex TagRegex =
        new(
            @"<\/?[^<>\r\n]+?>",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex PlaceholderRegex =
        new(
            @"\$\{[^{}\r\n]+\}|(?<!\$)\{[A-Za-z0-9_]+(?:[^{}\r\n]*)?\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc]",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    public static StructuralQaResult Analyze(
        string original,
        string translation)
    {
        original ??= string.Empty;
        translation ??= string.Empty;

        var sourceTags = Extract(TagRegex, original);
        var targetTags = Extract(TagRegex, translation);
        var sourcePlaceholders = Extract(PlaceholderRegex, original);
        var targetPlaceholders = Extract(PlaceholderRegex, translation);

        var missingTags = Difference(sourceTags, targetTags);
        var extraTags = Difference(targetTags, sourceTags);
        var missingPlaceholders =
            Difference(sourcePlaceholders, targetPlaceholders);
        var extraPlaceholders =
            Difference(targetPlaceholders, sourcePlaceholders);

        var tagOrderMismatch =
            missingTags.Count == 0 &&
            extraTags.Count == 0 &&
            !sourceTags.SequenceEqual(
                targetTags,
                StringComparer.Ordinal);

        var parts = new List<string>();

        if (missingTags.Count > 0 || extraTags.Count > 0 || tagOrderMismatch)
        {
            var details = new List<string>();

            if (missingTags.Count > 0)
                details.Add("отсутствует: " + FormatTokens(missingTags));

            if (extraTags.Count > 0)
                details.Add("лишнее: " + FormatTokens(extraTags));

            if (tagOrderMismatch)
                details.Add("порядок тегов отличается от оригинала");

            parts.Add("Теги — " + string.Join("; ", details));
        }

        if (missingPlaceholders.Count > 0 || extraPlaceholders.Count > 0)
        {
            var details = new List<string>();

            if (missingPlaceholders.Count > 0)
            {
                details.Add(
                    "отсутствует: " +
                    FormatTokens(missingPlaceholders));
            }

            if (extraPlaceholders.Count > 0)
            {
                details.Add(
                    "лишнее: " +
                    FormatTokens(extraPlaceholders));
            }

            parts.Add(
                "Плейсхолдеры — " +
                string.Join("; ", details));
        }

        return new StructuralQaResult(
            parts.Count > 0,
            parts.Count == 0
                ? "Структура тегов и плейсхолдеров совпадает с оригиналом."
                : string.Join("   •   ", parts),
            missingTags,
            extraTags,
            missingPlaceholders,
            extraPlaceholders,
            tagOrderMismatch);
    }

    private static List<string> Extract(
        Regex regex,
        string text)
        => regex
            .Matches(text)
            .Select(match => match.Value)
            .ToList();

    private static List<string> Difference(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var token in target)
        {
            counts[token] =
                counts.TryGetValue(token, out var count)
                    ? count + 1
                    : 1;
        }

        var result = new List<string>();

        foreach (var token in source)
        {
            if (counts.TryGetValue(token, out var count) && count > 0)
            {
                counts[token] = count - 1;
                continue;
            }

            result.Add(token);
        }

        return result;
    }

    private static string FormatTokens(IReadOnlyList<string> tokens)
    {
        var grouped = tokens
            .GroupBy(x => x, StringComparer.Ordinal)
            .Select(group =>
                group.Count() > 1
                    ? $"{group.Key} ×{group.Count()}"
                    : group.Key)
            .Take(8)
            .ToList();

        if (tokens.Distinct(StringComparer.Ordinal).Count() > grouped.Count)
            grouped.Add("…");

        return string.Join(", ", grouped);
    }
}
