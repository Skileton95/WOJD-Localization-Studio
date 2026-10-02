using System.Text.RegularExpressions;

namespace WOJD.LocalizationStudio.Services;

public sealed record TranslationValidationResult(
    int IssueCount,
    string Summary);

public static partial class TranslationValidator
{
    public static TranslationValidationResult Validate(
        string source,
        string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return new TranslationValidationResult(0, string.Empty);

        var issues = new List<string>();

        CompareTokens(
            source,
            translation,
            BracePlaceholderRegex(),
            "Плейсхолдеры {…} не совпадают",
            issues);

        CompareTokens(
            source,
            translation,
            PercentPlaceholderRegex(),
            "Плейсхолдеры %… не совпадают",
            issues);

        CompareTokens(
            source,
            translation,
            TagRegex(),
            "Теги <…> не совпадают",
            issues);

        var sourceNewLines = CountNewLines(source);
        var targetNewLines = CountNewLines(translation);

        if (sourceNewLines != targetNewLines)
        {
            issues.Add(
                $"Переносы строк: {sourceNewLines} → {targetNewLines}");
        }

        return new TranslationValidationResult(
            issues.Count,
            string.Join(" • ", issues));
    }

    private static void CompareTokens(
        string source,
        string translation,
        Regex regex,
        string message,
        ICollection<string> issues)
    {
        var sourceSignature = BuildSignature(source, regex);
        var targetSignature = BuildSignature(translation, regex);

        if (!string.Equals(
                sourceSignature,
                targetSignature,
                StringComparison.Ordinal))
        {
            issues.Add(message);
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
}
