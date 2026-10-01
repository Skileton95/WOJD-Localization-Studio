using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class ValidationService
{
    private static readonly Regex TokenRegex = new(
        @"\{\d+(?:[^{}]*)?\}|%(?:\d+\$)?[sd]|\\n|\r?\n|<\/?[A-Za-z][^>]*?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ChineseRegex = new(
        @"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DoubleSpaceRegex = new(
        @" {2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpaceBeforePunctuationRegex = new(
        @"[ \t]+[,\.!\?;:]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RepeatedPunctuationRegex = new(
        @"([,\.\!\?;:])\1+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (char Open, char Close)[] BracketPairs =
    [
        ('(', ')'),
        ('[', ']'),
        ('「', '」'),
        ('《', '》')
    ];

    public static void Validate(LocalizationEntry entry)
    {
        var issues = GetIssues(entry.Source, entry.Translation);
        entry.ValidationSummary = string.Join(Environment.NewLine, issues.Select(issue => issue.Message));
        entry.ValidationKinds = issues.Aggregate(
            ValidationIssueKind.None,
            (current, issue) => current | issue.Kind);
        entry.HasCriticalValidationIssues = issues.Any(issue => issue.IsCritical);
    }

    public static void ValidateAll(IEnumerable<LocalizationEntry> entries)
    {
        foreach (var entry in entries)
            Validate(entry);
    }

    public static IReadOnlyList<ValidationIssue> GetIssues(string source, string translation)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(translation))
            return issues;

        AddTechnicalIssues(source ?? string.Empty, translation, issues);
        AddChineseTextIssue(translation, issues);
        AddBracketIssues(source ?? string.Empty, translation, issues);
        AddWhitespaceIssues(translation, issues);
        AddPunctuationIssues(translation, issues);
        AddSourceCopyIssue(source ?? string.Empty, translation, issues);

        return issues;
    }

    public static bool HasIssueKind(LocalizationEntry entry, ValidationIssueKind kind) =>
        (entry.ValidationKinds & kind) != 0;

    public static string AutoFixTechnicalTokens(string source, string translation)
    {
        if (string.IsNullOrEmpty(translation))
            return translation;

        var requiredCounts = TokenRegex.Matches(source ?? string.Empty)
            .Cast<Match>()
            .Select(match => NormalizeToken(match.Value))
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var keptCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        var cleaned = TokenRegex.Replace(translation, match =>
        {
            var normalized = NormalizeToken(match.Value);
            requiredCounts.TryGetValue(normalized, out var required);

            keptCounts.TryGetValue(normalized, out var kept);
            if (kept >= required)
                return string.Empty;

            keptCounts[normalized] = kept + 1;
            return match.Value;
        });

        var missingRawTokens = new List<string>();
        var existingCounts = TokenRegex.Matches(cleaned)
            .Cast<Match>()
            .Select(match => NormalizeToken(match.Value))
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var consumedSourceCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match match in TokenRegex.Matches(source ?? string.Empty))
        {
            var normalized = NormalizeToken(match.Value);
            consumedSourceCounts.TryGetValue(normalized, out var sourceSeen);
            existingCounts.TryGetValue(normalized, out var existing);

            sourceSeen++;
            consumedSourceCounts[normalized] = sourceSeen;

            if (sourceSeen > existing)
                missingRawTokens.Add(match.Value);
        }

        if (missingRawTokens.Count == 0)
            return cleaned;

        var separator = cleaned.Length == 0 || char.IsWhiteSpace(cleaned[^1]) ? string.Empty : " ";
        return cleaned + separator + string.Join(" ", missingRawTokens);
    }

    public static string AutoFixDeterministic(string source, string translation)
    {
        var fixedText = AutoFixTechnicalTokens(source, translation);

        fixedText = DoubleSpaceRegex.Replace(fixedText, " ");
        fixedText = SpaceBeforePunctuationRegex.Replace(
            fixedText,
            match => match.Value.TrimStart());

        return fixedText;
    }

    public static string BuildSummary(string source, string translation) =>
        string.Join(Environment.NewLine, GetIssues(source, translation).Select(issue => issue.Message));

    public static IReadOnlyList<string> ExtractTokens(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return Array.Empty<string>();

        return TokenRegex.Matches(value)
            .Cast<Match>()
            .Select(match => NormalizeToken(match.Value))
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddTechnicalIssues(
        string source,
        string translation,
        ICollection<ValidationIssue> issues)
    {
        var sourceMatches = TokenRegex.Matches(source).Cast<Match>().ToList();
        var translationMatches = TokenRegex.Matches(translation).Cast<Match>().ToList();

        var sourceGroups = sourceMatches
            .GroupBy(match => NormalizeToken(match.Value), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var translationGroups = translationMatches
            .GroupBy(match => NormalizeToken(match.Value), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var pair in sourceGroups)
        {
            translationGroups.TryGetValue(pair.Key, out var translated);
            translated ??= [];

            for (var i = translated.Count; i < pair.Value.Count; i++)
            {
                var sourceMatch = pair.Value[i];
                issues.Add(new ValidationIssue(
                    ValidationIssueKind.Technical,
                    $"Потерян технический элемент {sourceMatch.Value} — в оригинале {ContextAt(source, sourceMatch.Index)}.",
                    true));
            }
        }

        foreach (var pair in translationGroups)
        {
            sourceGroups.TryGetValue(pair.Key, out var original);
            original ??= [];

            for (var i = original.Count; i < pair.Value.Count; i++)
            {
                var translationMatch = pair.Value[i];
                issues.Add(new ValidationIssue(
                    ValidationIssueKind.Technical,
                    $"Лишний технический элемент {translationMatch.Value} — в переводе {ContextAt(translation, translationMatch.Index)}.",
                    true));
            }
        }
    }

    private static void AddChineseTextIssue(
        string translation,
        ICollection<ValidationIssue> issues)
    {
        var match = ChineseRegex.Match(translation);
        if (!match.Success)
            return;

        issues.Add(new ValidationIssue(
            ValidationIssueKind.ChineseText,
            $"В русском переводе остался китайский текст — {ContextAt(translation, match.Index)}."));
    }

    private static void AddBracketIssues(
        string source,
        string translation,
        ICollection<ValidationIssue> issues)
    {
        foreach (var pair in BracketPairs)
        {
            AddBracketCharacterIssue(pair.Open, source, translation, issues);
            AddBracketCharacterIssue(pair.Close, source, translation, issues);
        }
    }

    private static void AddBracketCharacterIssue(
        char symbol,
        string source,
        string translation,
        ICollection<ValidationIssue> issues)
    {
        var sourceCount = source.Count(character => character == symbol);
        var translationCount = translation.Count(character => character == symbol);

        if (sourceCount == translationCount)
            return;

        issues.Add(new ValidationIssue(
            ValidationIssueKind.Brackets,
            $"Скобка «{symbol}»: в оригинале {sourceCount}, в переводе {translationCount}."));
    }

    private static void AddWhitespaceIssues(
        string translation,
        ICollection<ValidationIssue> issues)
    {
        var doubleSpace = DoubleSpaceRegex.Match(translation);
        if (doubleSpace.Success)
        {
            issues.Add(new ValidationIssue(
                ValidationIssueKind.Whitespace,
                $"Двойной пробел — {ContextAt(translation, doubleSpace.Index)}."));
        }

        var spaceBeforePunctuation = SpaceBeforePunctuationRegex.Match(translation);
        if (spaceBeforePunctuation.Success)
        {
            issues.Add(new ValidationIssue(
                ValidationIssueKind.Whitespace,
                $"Пробел перед знаком препинания — {ContextAt(translation, spaceBeforePunctuation.Index)}."));
        }
    }

    private static void AddPunctuationIssues(
        string translation,
        ICollection<ValidationIssue> issues)
    {
        var repeated = RepeatedPunctuationRegex.Match(translation);
        if (!repeated.Success)
            return;

        issues.Add(new ValidationIssue(
            ValidationIssueKind.Punctuation,
            $"Повторяющийся знак «{repeated.Value}» — {ContextAt(translation, repeated.Index)}."));
    }

    private static void AddSourceCopyIssue(
        string source,
        string translation,
        ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            !string.Equals(source.Trim(), translation.Trim(), StringComparison.Ordinal))
            return;

        issues.Add(new ValidationIssue(
            ValidationIssueKind.SourceCopy,
            "Русский перевод полностью совпадает с китайским оригиналом."));
    }

    private static string ContextAt(string text, int index)
    {
        if (string.IsNullOrEmpty(text))
            return "позиция неизвестна";

        var start = Math.Max(0, index - 14);
        var length = Math.Min(text.Length - start, 32);
        var snippet = text.Substring(start, length)
            .Replace("\r", " ")
            .Replace("\n", " ");

        if (start > 0)
            snippet = "…" + snippet;
        if (start + length < text.Length)
            snippet += "…";

        return $"позиция {index + 1}, рядом с «{snippet}»";
    }

    private static string NormalizeToken(string token) =>
        token is "\r\n" or "\n" or "\\n" ? "\\n" : token;
}
