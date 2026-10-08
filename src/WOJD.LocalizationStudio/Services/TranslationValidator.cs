using System.Text.RegularExpressions;

namespace WOJD.LocalizationStudio.Services;

public enum TranslationIssueKind
{
    Placeholder,
    Tag,
    NewLine,
    SameAsSource,
    SuspiciousLength,
    SourceMissing,
    ProfileRule,
    Glossary
}

public sealed record TranslationValidationResult(
    int IssueCount,
    string Summary,
    IReadOnlySet<TranslationIssueKind> Kinds);

public static partial class TranslationValidator
{
    private static readonly IReadOnlySet<TranslationIssueKind> EmptyKinds =
        new HashSet<TranslationIssueKind>();

    private static readonly TranslationValidationResult EmptyResult =
        new(0, string.Empty, EmptyKinds);

    public static TranslationValidationResult Validate(
        string source,
        string translation)
        => Validate(
            string.Empty,
            string.Empty,
            source,
            translation,
            applyProfiles: false);

    public static TranslationValidationResult Validate(
        string nameSpace,
        string key,
        string source,
        string translation)
        => Validate(
            nameSpace,
            key,
            source,
            translation,
            applyProfiles: true);

    private static TranslationValidationResult Validate(
        string nameSpace,
        string key,
        string source,
        string translation,
        bool applyProfiles)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return EmptyResult;

        var issues = new List<(TranslationIssueKind Kind, string Message)>();

        if (string.IsNullOrWhiteSpace(source))
        {
            issues.Add((
                TranslationIssueKind.SourceMissing,
                "Исходный текст отсутствует — содержимое и структура не могут быть проверены"));
        }
        else
        {
            // Regex signatures are comparatively expensive at 600k+ rows. Most game
            // strings do not contain structure at all, so reject them with a cheap
            // marker check before invoking the regex engine.
            if (ContainsEither(source, translation, '{'))
            {
                CompareTokens(
                    source,
                    translation,
                    BracePlaceholderRegex(),
                    TranslationIssueKind.Placeholder,
                    "Плейсхолдеры {…} не совпадают",
                    issues);
            }

            if (ContainsEither(source, translation, '%'))
            {
                CompareTokens(
                    source,
                    translation,
                    PercentPlaceholderRegex(),
                    TranslationIssueKind.Placeholder,
                    "Плейсхолдеры %… не совпадают",
                    issues);
            }

            if (ContainsEither(source, translation, '<'))
            {
                CompareTokens(
                    source,
                    translation,
                    TagRegex(),
                    TranslationIssueKind.Tag,
                    "Теги <…> не совпадают",
                    issues);
            }

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
                var ratio = (double)translationLength / sourceLength;

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

            foreach (var glossaryIssue in GlossaryService.Validate(source, translation))
            {
                issues.Add((
                    TranslationIssueKind.Glossary,
                    glossaryIssue));
            }
        }

        if (applyProfiles)
        {
            foreach (var profileIssue in
                     QaProfileService.Validate(
                         nameSpace,
                         key,
                         translation))
            {
                issues.Add((
                    TranslationIssueKind.ProfileRule,
                    profileIssue));
            }
        }

        if (issues.Count == 0)
            return EmptyResult;

        return new TranslationValidationResult(
            issues.Count,
            string.Join(" • ", issues.Select(x => x.Message)),
            issues.Select(x => x.Kind).ToHashSet());
    }

    private static bool ContainsEither(string left, string right, char marker)
        => left.IndexOf(marker) >= 0 || right.IndexOf(marker) >= 0;

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

        if (text.IndexOfAny(['{', '%', '<']) < 0)
        {
            var visible = 0;
            foreach (var ch in text)
            {
                if (!char.IsWhiteSpace(ch))
                    visible++;
            }
            return visible;
        }

        var value = BracePlaceholderRegex().Replace(text, string.Empty);
        value = PercentPlaceholderRegex().Replace(value, string.Empty);
        value = TagRegex().Replace(value, string.Empty);

        return value.Count(x => !char.IsWhiteSpace(x));
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if ((ch >= '\u3400' && ch <= '\u4DBF') ||
                (ch >= '\u4E00' && ch <= '\u9FFF') ||
                (ch >= '\uF900' && ch <= '\uFAFF'))
            {
                return true;
            }
        }

        return false;
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

    [GeneratedRegex(@"\{[A-Za-z0-9_]+(?:[^{}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex BracePlaceholderRegex();

    [GeneratedRegex(@"%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc]", RegexOptions.CultureInvariant)]
    private static partial Regex PercentPlaceholderRegex();

    [GeneratedRegex(@"<\/?[^<>]+?>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();
}
