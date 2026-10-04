using System.Text.RegularExpressions;

namespace WOJD.LocalizationStudio.Services;

public sealed record StructuralDiffResult(
    int SourceTags,
    int TargetTags,
    int SourcePlaceholders,
    int TargetPlaceholders,
    int SourceNewLines,
    int TargetNewLines,
    IReadOnlyList<string> MissingTokens,
    IReadOnlyList<string> ExtraTokens,
    bool OrderMismatch)
{
    public bool HasIssues
        => MissingTokens.Count > 0 ||
           ExtraTokens.Count > 0 ||
           OrderMismatch ||
           SourceNewLines != TargetNewLines;
}

public sealed record HighlightSegment(
    string Text,
    bool IsToken,
    bool IsProblem);

public static partial class StructuralDiffService
{
    public static StructuralDiffResult Analyze(
        string source,
        string translation)
    {
        source ??= string.Empty;
        translation ??= string.Empty;

        var sourceTokens = ExtractTokens(source);
        var targetTokens = ExtractTokens(translation);
        var missing = Difference(sourceTokens, targetTokens);
        var extra = Difference(targetTokens, sourceTokens);

        var sourceKnown = sourceTokens.Where(x => targetTokens.Contains(x, StringComparer.Ordinal)).ToList();
        var targetKnown = targetTokens.Where(x => sourceTokens.Contains(x, StringComparer.Ordinal)).ToList();
        var orderMismatch =
            sourceKnown.Count == targetKnown.Count &&
            sourceKnown.Count > 1 &&
            !sourceKnown.SequenceEqual(targetKnown, StringComparer.Ordinal);

        return new StructuralDiffResult(
            TagRegex().Matches(source).Count,
            TagRegex().Matches(translation).Count,
            PlaceholderRegex().Matches(source).Count,
            PlaceholderRegex().Matches(translation).Count,
            CountNewLines(source),
            CountNewLines(translation),
            missing,
            extra,
            orderMismatch);
    }

    public static IReadOnlyList<HighlightSegment> TokenizeForPreview(
        string text,
        StructuralDiffResult? diff = null,
        bool translationSide = true)
    {
        text ??= string.Empty;
        var matches = TokenRegex().Matches(text);
        if (matches.Count == 0)
            return text.Length == 0 ? [] : [new HighlightSegment(text, false, false)];

        var problemTokens = new HashSet<string>(
            translationSide
                ? diff?.ExtraTokens ?? []
                : diff?.MissingTokens ?? [],
            StringComparer.Ordinal);

        var result = new List<HighlightSegment>();
        var cursor = 0;

        foreach (Match match in matches)
        {
            if (match.Index > cursor)
            {
                result.Add(new HighlightSegment(
                    text[cursor..match.Index],
                    false,
                    false));
            }

            result.Add(new HighlightSegment(
                match.Value,
                true,
                problemTokens.Contains(match.Value)));
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            result.Add(new HighlightSegment(text[cursor..], false, false));

        return result;
    }

    public static IReadOnlyList<string> ExtractTokens(string text)
        => TokenRegex()
            .Matches(text ?? string.Empty)
            .Select(x => x.Value)
            .ToList();

    private static List<string> Difference(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var remaining = right
            .GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var token in left)
        {
            if (remaining.TryGetValue(token, out var count) && count > 0)
            {
                remaining[token] = count - 1;
                continue;
            }

            result.Add(token);
        }

        return result;
    }

    private static int CountNewLines(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                count++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
            }
            else if (text[i] == '\n')
            {
                count++;
            }
            else if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'n')
            {
                count++;
                i++;
            }
        }

        return count;
    }

    [GeneratedRegex(@"(<\/?[^<>\r\n]+?>|\$\{[^{}\r\n]+\}|(?<!\$)\{[A-Za-z0-9_]+(?:[^{}\r\n]*)?\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc]|\\[nrt])",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"<\/?[^<>\r\n]+?>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"(\$\{[^{}\r\n]+\}|(?<!\$)\{[A-Za-z0-9_]+(?:[^{}\r\n]*)?\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
