using System.Text.RegularExpressions;

namespace WOJD.LocalizationStudio.Services;

public enum TranslationIssueKind
{
    Placeholder,
    Tag,
    NewLine,
    SameAsSource,
    SuspiciousLength
}

public sealed record TranslationValidationResult(
    int IssueCount,
    string Summary,
    IReadOnlySet<TranslationIssueKind> Kinds);

public static partial class TranslationValidator
{
    public static TranslationValidationResult Validate(
        string source,
        string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
        {
            return new TranslationValidationResult(
                0,
                string.Empty,
                new HashSet<TranslationIssueKind>());
        }

        var issues = new List<(TranslationIssueKind Kind, string Message)>();

        CompareTokens(
            source,
            translation,
            BracePlaceholderRegex(),
            TranslationIssueKind.Placeholder,
            "Плейсхолдеры {…} не совпадают",
            issues);

        CompareTokens(
            source,
            translation,
            PercentPlaceholderRegex(),
            TranslationIssueKind.Placeholder,
            "Плейсхолдеры %… не совпадают",
            issues);

        CompareTokens(
            source,
            translation,
            TagRegex(),
            TranslationIssueKind.Tag,
            "Теги <…> не совпадают",
            issues);

        var sourceNewLines = CountNewLines(source);
        var targetNewLines = CountNewLines(translation);

        if (sourceNewLines != targetNewLines)
        {
            issues.Add((
                TranslationIssueKind.NewLine,
                $"Переносы строк: {sourceNewLines} → {targetNewLines}"));
        }

        var trimmedSource = source.Trim();
        var trimmedTranslation = translation.Trim();

        if (ContainsCjk(trimmedSource) &&
            string.Equals(
                trimmedSource,
                trimmedTranslation,
                StringComparison.Ordinal))
        {
            issues.Add((
                TranslationIssueKind.SameAsSource,
                "Перевод полностью совпадает с китайским оригиналом"));
        }

        var sourceLength = GetVisibleLength(source);
        var translationLength = GetVisibleLength(translation);

        if (sourceLength >= 8 && translationLength > 0)
        {
            var ratio =
                (double)translationLength / sourceLength;

            if (ratio < 0.35)
            {
                issues.Add((
                    TranslationIssueKind.SuspiciousLength,
                    $"Подозрительно короткий перевод: {sourceLength} → {translationLength} символов"));
            }
            else if (ratio > 8.0)
            {
                issues.Add((
                    TranslationIssueKind.SuspiciousLength,
                    $"Подозрительно длинный перевод: {sourceLength} → {translationLength} символов"));
            }
        }

        return new TranslationValidationResult(
            issues.Count,
            string.Join(" • ", issues.Select(x => x.Message)),
            issues.Select(x => x.Kind).ToHashSet());
    }

    private static void CompareTokens(
        string source,
        string translation,
        Regex regex,
        TranslationIssueKind kind,
        string message,
        ICollection<(TranslationIssueKind Kind, string Message)> issues)
    {
        var sourceSignature = BuildSignature(source, regex);
        var targetSignature = BuildSignature(translation, regex);

        if (!string.Equals(
                sourceSignature,
                targetSignature,
                StringComparison.Ordinal))
        {
            issues.Add((kind, message));
        }
    }

    private static string BuildSignature(
        string text,
        Regex regex)
        => string.Join(
            "\u001F",
            regex.Matches(text)
                .Select(x => x.Value)
                .OrderBy(x => x, StringComparer.Ordinal));

    private static int GetVisibleLength(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var value = BracePlaceholderRegex().Replace(text, string.Empty);
        value = PercentPlaceholderRegex().Replace(value, string.Empty);
        value = TagRegex().Replace(value, string.Empty);

        return value.Count(x => !char.IsWhiteSpace(x));
    }

    private static bool ContainsCjk(string text)
        => CjkRegex().IsMatch(text);

    private static int CountNewLines(string text)
    {
        var count = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                count++;

                if (i + 1 < text.Length &&
                    text[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            if (text[i] == '\n')
            {
                count++;
                continue;
            }

            if (text[i] == '\\' &&
                i + 1 < text.Length &&
                text[i + 1] == 'n')
            {
                count++;
                i++;
            }
        }

        return count;
    }

    [GeneratedRegex(@"\{[A-Za-z0-9_]+(?:[^{}]*)?\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex BracePlaceholderRegex();

    [GeneratedRegex(@"%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc]",
        RegexOptions.CultureInvariant)]
    private static partial Regex PercentPlaceholderRegex();

    [GeneratedRegex(@"<\/?[^<>]+?>",
        RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]",
        RegexOptions.CultureInvariant)]
    private static partial Regex CjkRegex();
}
