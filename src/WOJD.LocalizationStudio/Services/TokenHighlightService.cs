using System.Text.RegularExpressions;

namespace WOJD.LocalizationStudio.Services;

public enum HighlightTokenKind
{
    Text,
    Tag,
    Placeholder,
    Escape
}

public sealed record HighlightSegment(
    string Text,
    HighlightTokenKind Kind);

public static class TokenHighlightService
{
    private static readonly Regex TokenRegex =
        new(
            @"(?<tag><\/?[^<>\r\n]+?>)|(?<dollar>\$\{[^{}\r\n]+\})|(?<brace>(?<!\$)\{[A-Za-z0-9_]+(?:[^{}\r\n]*)?\})|(?<percent>%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc])|(?<escape>\\[nrt])",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    public static IReadOnlyList<HighlightSegment> Parse(string? text)
    {
        text ??= string.Empty;

        if (text.Length == 0)
            return [];

        var result = new List<HighlightSegment>();
        var cursor = 0;

        foreach (Match match in TokenRegex.Matches(text))
        {
            if (match.Index > cursor)
            {
                result.Add(
                    new HighlightSegment(
                        text[cursor..match.Index],
                        HighlightTokenKind.Text));
            }

            var kind = match.Groups["tag"].Success
                ? HighlightTokenKind.Tag
                : match.Groups["escape"].Success
                    ? HighlightTokenKind.Escape
                    : HighlightTokenKind.Placeholder;

            result.Add(new HighlightSegment(match.Value, kind));
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            result.Add(new HighlightSegment(text[cursor..], HighlightTokenKind.Text));

        return result;
    }
}
