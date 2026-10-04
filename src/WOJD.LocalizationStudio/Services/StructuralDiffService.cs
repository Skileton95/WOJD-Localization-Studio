using System.Text.RegularExpressions;

namespace WOJD.LocalizationStudio.Services;

public enum StructuralTokenKind
{
    Text,
    Tag,
    Placeholder,
    NewLine
}

public sealed record StructuralToken(
    StructuralTokenKind Kind,
    string Value,
    int Start,
    int Length);

public sealed record StructuralDiffResult(
    IReadOnlyList<StructuralToken> SourceTokens,
    IReadOnlyList<StructuralToken> TranslationTokens,
    int SourceTagCount,
    int TranslationTagCount,
    int SourcePlaceholderCount,
    int TranslationPlaceholderCount,
    int SourceNewLineCount,
    int TranslationNewLineCount,
    StructuralQaResult Qa)
{
    public bool HasIssues
        => Qa.HasIssues || SourceNewLineCount != TranslationNewLineCount;
}

public static class StructuralDiffService
{
    private static readonly Regex TokenRegex = new(
        @"<\/?[^<>\r\n]+?>|\$\{[^{}\r\n]+\}|(?<!\$)\{[A-Za-z0-9_]+(?:[^{}\r\n]*)?\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc]|\r\n|\r|\n|\\n",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TagRegex = new(
        @"^<\/?[^<>\r\n]+?>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlaceholderRegex = new(
        @"^(?:\$\{[^{}\r\n]+\}|(?<!\$)\{[A-Za-z0-9_]+(?:[^{}\r\n]*)?\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc])$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static StructuralDiffResult Analyze(string original, string translation)
    {
        original ??= string.Empty;
        translation ??= string.Empty;

        var sourceTokens = Tokenize(original);
        var translationTokens = Tokenize(translation);

        return new StructuralDiffResult(
            sourceTokens,
            translationTokens,
            sourceTokens.Count(x => x.Kind == StructuralTokenKind.Tag),
            translationTokens.Count(x => x.Kind == StructuralTokenKind.Tag),
            sourceTokens.Count(x => x.Kind == StructuralTokenKind.Placeholder),
            translationTokens.Count(x => x.Kind == StructuralTokenKind.Placeholder),
            sourceTokens.Count(x => x.Kind == StructuralTokenKind.NewLine),
            translationTokens.Count(x => x.Kind == StructuralTokenKind.NewLine),
            StructuralQaService.Analyze(original, translation));
    }

    public static IReadOnlyList<StructuralToken> Tokenize(string text)
    {
        text ??= string.Empty;
        var result = new List<StructuralToken>();
        var cursor = 0;

        foreach (Match match in TokenRegex.Matches(text))
        {
            if (match.Index > cursor)
            {
                result.Add(new StructuralToken(
                    StructuralTokenKind.Text,
                    text[cursor..match.Index],
                    cursor,
                    match.Index - cursor));
            }

            var kind = TagRegex.IsMatch(match.Value)
                ? StructuralTokenKind.Tag
                : PlaceholderRegex.IsMatch(match.Value)
                    ? StructuralTokenKind.Placeholder
                    : StructuralTokenKind.NewLine;

            result.Add(new StructuralToken(
                kind,
                match.Value,
                match.Index,
                match.Length));
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
        {
            result.Add(new StructuralToken(
                StructuralTokenKind.Text,
                text[cursor..],
                cursor,
                text.Length - cursor));
        }

        return result;
    }
}
