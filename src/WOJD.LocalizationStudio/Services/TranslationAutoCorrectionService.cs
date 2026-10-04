using System.Text;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public interface ITranslationCorrectionProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }

    TranslationCorrectionResult Correct(LocalizationEntry entry);
}

public sealed record TranslationCorrectionResult(
    string CorrectedText,
    int FixCount,
    IReadOnlyList<string> AppliedRules)
{
    public bool HasChanges { get; init; }
}

public sealed record TranslationCorrectionChange(
    LocalizationEntry Entry,
    string Before,
    string After,
    int FixCount,
    IReadOnlyList<string> AppliedRules);

public sealed class TranslationCorrectionPlan
{
    public TranslationCorrectionPlan(
        IReadOnlyList<TranslationCorrectionChange> changes)
    {
        Changes = changes;
    }

    public IReadOnlyList<TranslationCorrectionChange> Changes { get; }
    public int AffectedEntries => Changes.Count;
    public int TotalFixes => Changes.Sum(x => x.FixCount);

    public TranslationCorrectionApplyResult Apply()
    {
        var applied = 0;
        var skipped = 0;

        foreach (var change in Changes)
        {
            // План строится до применения. Если строка успела измениться,
            // не перезаписываем более свежий перевод.
            if (!string.Equals(
                    change.Entry.Translation,
                    change.Before,
                    StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            change.Entry.Translation = change.After;
            applied++;
        }

        return new TranslationCorrectionApplyResult(
            applied,
            skipped);
    }
}

public sealed record TranslationCorrectionApplyResult(
    int AppliedEntries,
    int SkippedEntries);

public static class TranslationAutoCorrectionService
{
    public static ITranslationCorrectionProvider RuleBased { get; }
        = new RuleBasedTranslationCorrectionProvider();

    public static TranslationCorrectionPlan BuildPlan(
        IEnumerable<LocalizationEntry> entries,
        ITranslationCorrectionProvider? provider = null,
        CancellationToken cancellationToken = default)
    {
        provider ??= RuleBased;

        if (!provider.IsAvailable)
            return new TranslationCorrectionPlan([]);

        var changes =
            new List<TranslationCorrectionChange>();

        foreach (var entry in entries.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.Translation))
                continue;

            var before = entry.Translation;
            var result = provider.Correct(entry);

            if (!result.HasChanges ||
                string.Equals(
                    before,
                    result.CorrectedText,
                    StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add(
                new TranslationCorrectionChange(
                    entry,
                    before,
                    result.CorrectedText,
                    result.FixCount,
                    result.AppliedRules));
        }

        return new TranslationCorrectionPlan(changes);
    }
}

public sealed class RuleBasedTranslationCorrectionProvider
    : ITranslationCorrectionProvider
{
    private static readonly Regex ProtectedTokenRegex =
        new(
            @"(<[^>\r\n]+>|\$\{[^}\r\n]*\}|\{[^{}\r\n]*\}|%\d*\$?[a-zA-Z]|\\[nrt])",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex TrailingWhitespaceRegex =
        new(
            @"[ \t]+(?=\r?$)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.Multiline);

    private static readonly Regex MultipleSpacesRegex =
        new(
            @" {2,}",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex SpaceBeforePunctuationRegex =
        new(
            @" +(?=[,.;:!?])",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex SpaceAfterOpeningBracketRegex =
        new(
            @"(?<=[\(\[\{«]) +",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex SpaceBeforeClosingBracketRegex =
        new(
            @" +(?=[\)\]\}»])",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    public string Id => "rules-v1";
    public string DisplayName => "Автоисправление";
    public bool IsAvailable => true;

    public TranslationCorrectionResult Correct(
        LocalizationEntry entry)
    {
        var source = entry.Translation;

        if (string.IsNullOrEmpty(source))
        {
            return new TranslationCorrectionResult(
                source,
                0,
                []);
        }

        var builder =
            new StringBuilder(source.Length);

        var appliedRules =
            new HashSet<string>(
                StringComparer.Ordinal);

        var fixCount = 0;
        var cursor = 0;

        foreach (Match match in
                 ProtectedTokenRegex.Matches(source))
        {
            if (match.Index > cursor)
            {
                builder.Append(
                    CorrectPlainText(
                        source[cursor..match.Index],
                        appliedRules,
                        ref fixCount));
            }

            // Теги и плейсхолдеры копируются байт-в-байт по символам.
            builder.Append(match.Value);
            cursor = match.Index + match.Length;
        }

        if (cursor < source.Length)
        {
            builder.Append(
                CorrectPlainText(
                    source[cursor..],
                    appliedRules,
                    ref fixCount));
        }

        var corrected = builder.ToString();

        return new TranslationCorrectionResult(
            corrected,
            fixCount,
            appliedRules.ToArray())
        {
            HasChanges =
                !string.Equals(
                    source,
                    corrected,
                    StringComparison.Ordinal)
        };
    }

    private static string CorrectPlainText(
        string text,
        ISet<string> appliedRules,
        ref int fixCount)
    {
        var value = text;

        value = ApplyRule(
            value,
            TrailingWhitespaceRegex,
            string.Empty,
            "Хвостовые пробелы",
            appliedRules,
            ref fixCount);

        value = ApplyRule(
            value,
            MultipleSpacesRegex,
            " ",
            "Двойные пробелы",
            appliedRules,
            ref fixCount);

        value = ApplyRule(
            value,
            SpaceBeforePunctuationRegex,
            string.Empty,
            "Пробел перед пунктуацией",
            appliedRules,
            ref fixCount);

        value = ApplyRule(
            value,
            SpaceAfterOpeningBracketRegex,
            string.Empty,
            "Пробел после открывающей скобки",
            appliedRules,
            ref fixCount);

        value = ApplyRule(
            value,
            SpaceBeforeClosingBracketRegex,
            string.Empty,
            "Пробел перед закрывающей скобкой",
            appliedRules,
            ref fixCount);

        return value;
    }

    private static string ApplyRule(
        string value,
        Regex regex,
        string replacement,
        string ruleName,
        ISet<string> appliedRules,
        ref int fixCount)
    {
        var matches = regex.Matches(value);

        if (matches.Count == 0)
            return value;

        fixCount += matches.Count;
        appliedRules.Add(ruleName);

        return regex.Replace(
            value,
            replacement);
    }
}
