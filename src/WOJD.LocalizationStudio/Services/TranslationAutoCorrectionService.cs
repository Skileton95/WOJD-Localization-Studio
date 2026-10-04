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
    public bool RequiresReview { get; init; }
    public string ReviewReason { get; init; } = string.Empty;
}

public sealed record TranslationCorrectionChange(
    LocalizationEntry Entry,
    string Before,
    string After,
    int FixCount,
    IReadOnlyList<string> AppliedRules);

public sealed record TranslationCorrectionReviewItem(
    LocalizationEntry Entry,
    string Reason);

public sealed class TranslationCorrectionPlan
{
    public TranslationCorrectionPlan(
        IReadOnlyList<TranslationCorrectionChange> changes,
        IReadOnlyList<TranslationCorrectionReviewItem>? reviewItems = null)
    {
        Changes = changes;
        ReviewItems = reviewItems ?? Array.Empty<TranslationCorrectionReviewItem>();
    }

    public IReadOnlyList<TranslationCorrectionChange> Changes { get; }
    public IReadOnlyList<TranslationCorrectionReviewItem> ReviewItems { get; }
    public int AffectedEntries => Changes.Count;
    public int TotalFixes => Changes.Sum(x => x.FixCount);

    public TranslationCorrectionApplyResult Apply(
        IEnumerable<TranslationCorrectionChange>? selectedChanges = null)
    {
        var applied = 0;
        var skipped = 0;
        var appliedChanges = new List<TranslationCorrectionChange>();

        foreach (var change in selectedChanges ?? Changes)
        {
            if (!string.Equals(
                    change.Entry.Translation,
                    change.Before,
                    StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            change.Entry.Translation = change.After;
            appliedChanges.Add(change);
            applied++;
        }

        return new TranslationCorrectionApplyResult(
            applied,
            skipped,
            appliedChanges);
    }
}

public sealed record TranslationCorrectionApplyResult(
    int AppliedEntries,
    int SkippedEntries,
    IReadOnlyList<TranslationCorrectionChange> AppliedChanges);

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

        var changes = new List<TranslationCorrectionChange>();
        var reviewItems = new List<TranslationCorrectionReviewItem>();

        foreach (var entry in entries.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.Translation))
                continue;

            var before = entry.Translation;
            var result = provider.Correct(entry);

            if (result.RequiresReview)
            {
                reviewItems.Add(
                    new TranslationCorrectionReviewItem(
                        entry,
                        result.ReviewReason));
            }

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

        return new TranslationCorrectionPlan(
            changes,
            reviewItems);
    }
}

public sealed class RuleBasedTranslationCorrectionProvider
    : ITranslationCorrectionProvider
{
    private static readonly Regex ProtectedTokenRegex =
        new(
            @"(<\/?[^<>\r\n]+?>|\$\{[^{}\r\n]+\}|(?<!\$)\{[A-Za-z0-9_]+(?:[^{}\r\n]*)?\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc]|\\[nrt])",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex StructuralTokenRegex =
        new(
            @"(?<tag><\/?[^<>\r\n]+?>)|(?<dollar>\$\{[^{}\r\n]+\})|(?<brace>(?<!\$)\{[A-Za-z0-9_]+(?:[^{}\r\n]*)?\})|(?<percent>%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc])|(?<escape>\\[nrt])",
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

    public string Id => "rules-v2";
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

        var appliedRules =
            new HashSet<string>(StringComparer.Ordinal);

        var fixCount = 0;
        var structure =
            RepairStructure(
                entry.Original,
                source,
                appliedRules,
                ref fixCount);

        var corrected =
            CorrectProtectedText(
                structure.Text,
                appliedRules,
                ref fixCount);

        return new TranslationCorrectionResult(
            corrected,
            fixCount,
            appliedRules.ToArray())
        {
            HasChanges =
                !string.Equals(
                    source,
                    corrected,
                    StringComparison.Ordinal),
            RequiresReview = structure.RequiresReview,
            ReviewReason = structure.ReviewReason
        };
    }

    private static StructureRepairResult RepairStructure(
        string original,
        string translation,
        ISet<string> appliedRules,
        ref int fixCount)
    {
        var sourceTokens = ParseTokens(original);
        var targetTokens = ParseTokens(translation);

        if (sourceTokens.Count == 0 && targetTokens.Count == 0)
            return new StructureRepairResult(translation, false, string.Empty);

        if (HasMalformedTokenFragments(translation, targetTokens))
        {
            return new StructureRepairResult(
                translation,
                true,
                "Похоже на повреждённый или незакрытый тег/плейсхолдер");
        }

        if (SequenceEqual(sourceTokens, targetTokens))
            return new StructureRepairResult(translation, false, string.Empty);

        if (SameMultiset(sourceTokens, targetTokens))
        {
            var sourceTags = sourceTokens
                .Where(x => x.Kind == StructuralTokenKind.Tag)
                .Select(x => x.Value)
                .ToArray();
            var targetTags = targetTokens
                .Where(x => x.Kind == StructuralTokenKind.Tag)
                .Select(x => x.Value)
                .ToArray();

            if (!sourceTags.SequenceEqual(targetTags, StringComparer.Ordinal))
            {
                return new StructureRepairResult(
                    translation,
                    true,
                    "Теги присутствуют, но их порядок отличается от оригинала");
            }

            // Плейсхолдеры могут менять порядок из-за грамматики перевода.
            return new StructureRepairResult(translation, false, string.Empty);
        }

        if (sourceTokens.Count == targetTokens.Count)
        {
            var mismatches = new List<int>();

            for (var i = 0; i < sourceTokens.Count; i++)
            {
                if (!string.Equals(
                        sourceTokens[i].Value,
                        targetTokens[i].Value,
                        StringComparison.Ordinal))
                {
                    mismatches.Add(i);
                }
            }

            if (mismatches.Count == 1)
            {
                var index = mismatches[0];

                if (sourceTokens[index].Kind == targetTokens[index].Kind)
                {
                    appliedRules.Add(GetRestoreRuleName(sourceTokens[index].Kind));
                    fixCount++;

                    return new StructureRepairResult(
                        ReplaceToken(
                            translation,
                            targetTokens[index],
                            sourceTokens[index].Value),
                        false,
                        string.Empty);
                }
            }

            return new StructureRepairResult(
                translation,
                true,
                "Несколько тегов или плейсхолдеров отличаются от оригинала");
        }

        if (sourceTokens.Count == targetTokens.Count + 1)
        {
            if (CanAlignPrefix(sourceTokens, targetTokens, out var prefixMismatch))
            {
                var value = translation;

                if (prefixMismatch >= 0)
                {
                    value = ReplaceToken(
                        value,
                        ParseTokens(value)[prefixMismatch],
                        sourceTokens[prefixMismatch].Value);
                    appliedRules.Add(GetRestoreRuleName(sourceTokens[prefixMismatch].Kind));
                    fixCount++;
                }

                value += sourceTokens[^1].Value;
                appliedRules.Add(GetRestoreRuleName(sourceTokens[^1].Kind));
                fixCount++;

                return new StructureRepairResult(value, false, string.Empty);
            }

            if (CanAlignSuffix(sourceTokens, targetTokens, out var suffixMismatch))
            {
                var value = translation;

                if (suffixMismatch >= 0)
                {
                    var current = ParseTokens(value);
                    value = ReplaceToken(
                        value,
                        current[suffixMismatch],
                        sourceTokens[suffixMismatch + 1].Value);
                    appliedRules.Add(GetRestoreRuleName(sourceTokens[suffixMismatch + 1].Kind));
                    fixCount++;
                }

                value = sourceTokens[0].Value + value;
                appliedRules.Add(GetRestoreRuleName(sourceTokens[0].Kind));
                fixCount++;

                return new StructureRepairResult(value, false, string.Empty);
            }
        }

        if (targetTokens.Count == 0)
        {
            if (sourceTokens.Count == 1)
            {
                var token = sourceTokens[0];

                if (token.Index == 0)
                {
                    appliedRules.Add(GetRestoreRuleName(token.Kind));
                    fixCount++;
                    return new StructureRepairResult(
                        token.Value + translation,
                        false,
                        string.Empty);
                }

                if (token.Index + token.Length == original.Length)
                {
                    appliedRules.Add(GetRestoreRuleName(token.Kind));
                    fixCount++;
                    return new StructureRepairResult(
                        translation + token.Value,
                        false,
                        string.Empty);
                }
            }

            if (sourceTokens.Count == 2 &&
                sourceTokens[0].Kind == StructuralTokenKind.Tag &&
                sourceTokens[1].Kind == StructuralTokenKind.Tag &&
                sourceTokens[0].Index == 0 &&
                sourceTokens[1].Index + sourceTokens[1].Length == original.Length)
            {
                appliedRules.Add("Восстановлены теги по оригиналу");
                fixCount += 2;

                return new StructureRepairResult(
                    sourceTokens[0].Value + translation + sourceTokens[1].Value,
                    false,
                    string.Empty);
            }
        }

        return new StructureRepairResult(
            translation,
            true,
            "Позицию отсутствующего тега или плейсхолдера нельзя определить безопасно");
    }

    private static string CorrectProtectedText(
        string source,
        ISet<string> appliedRules,
        ref int fixCount)
    {
        var builder = new StringBuilder(source.Length);
        var cursor = 0;

        foreach (Match match in ProtectedTokenRegex.Matches(source))
        {
            if (match.Index > cursor)
            {
                builder.Append(
                    CorrectPlainText(
                        source[cursor..match.Index],
                        appliedRules,
                        ref fixCount));
            }

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

        return builder.ToString();
    }

    private static List<StructuralToken> ParseTokens(string text)
    {
        var result = new List<StructuralToken>();

        foreach (Match match in StructuralTokenRegex.Matches(text))
        {
            var kind = match.Groups["tag"].Success
                ? StructuralTokenKind.Tag
                : match.Groups["dollar"].Success
                    ? StructuralTokenKind.DollarBrace
                    : match.Groups["brace"].Success
                        ? StructuralTokenKind.Brace
                        : match.Groups["percent"].Success
                            ? StructuralTokenKind.Percent
                            : StructuralTokenKind.Escape;

            result.Add(
                new StructuralToken(
                    match.Value,
                    kind,
                    match.Index,
                    match.Length));
        }

        return result;
    }

    private static bool HasMalformedTokenFragments(
        string translation,
        IReadOnlyList<StructuralToken> tokens)
    {
        if (string.IsNullOrEmpty(translation))
            return false;

        var mask = translation.ToCharArray();

        foreach (var token in tokens)
        {
            for (var i = token.Index;
                 i < token.Index + token.Length && i < mask.Length;
                 i++)
            {
                mask[i] = ' ';
            }
        }

        var remainder = new string(mask);

        return remainder.Contains('<') ||
               remainder.Contains('>') ||
               remainder.Contains("${", StringComparison.Ordinal);
    }

    private static bool SequenceEqual(
        IReadOnlyList<StructuralToken> left,
        IReadOnlyList<StructuralToken> right)
        => left.Count == right.Count &&
           left.Select(x => x.Value)
               .SequenceEqual(
                   right.Select(x => x.Value),
                   StringComparer.Ordinal);

    private static bool SameMultiset(
        IReadOnlyList<StructuralToken> left,
        IReadOnlyList<StructuralToken> right)
    {
        if (left.Count != right.Count)
            return false;

        return left.Select(x => x.Value)
            .OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(
                right.Select(x => x.Value)
                    .OrderBy(x => x, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static bool CanAlignPrefix(
        IReadOnlyList<StructuralToken> source,
        IReadOnlyList<StructuralToken> target,
        out int mismatchIndex)
    {
        mismatchIndex = -1;

        if (source.Count != target.Count + 1)
            return false;

        for (var i = 0; i < target.Count; i++)
        {
            if (source[i].Kind != target[i].Kind)
                return false;

            if (string.Equals(source[i].Value, target[i].Value, StringComparison.Ordinal))
                continue;

            if (mismatchIndex >= 0)
                return false;

            mismatchIndex = i;
        }

        return true;
    }

    private static bool CanAlignSuffix(
        IReadOnlyList<StructuralToken> source,
        IReadOnlyList<StructuralToken> target,
        out int mismatchIndex)
    {
        mismatchIndex = -1;

        if (source.Count != target.Count + 1)
            return false;

        for (var i = 0; i < target.Count; i++)
        {
            var sourceIndex = i + 1;

            if (source[sourceIndex].Kind != target[i].Kind)
                return false;

            if (string.Equals(source[sourceIndex].Value, target[i].Value, StringComparison.Ordinal))
                continue;

            if (mismatchIndex >= 0)
                return false;

            mismatchIndex = i;
        }

        return true;
    }

    private static string ReplaceToken(
        string text,
        StructuralToken token,
        string replacement)
        => text[..token.Index] +
           replacement +
           text[(token.Index + token.Length)..];

    private static string GetRestoreRuleName(StructuralTokenKind kind)
        => kind switch
        {
            StructuralTokenKind.Tag => "Восстановлен тег по оригиналу",
            StructuralTokenKind.DollarBrace => "Восстановлен ${…} по оригиналу",
            StructuralTokenKind.Brace => "Восстановлен плейсхолдер {…} по оригиналу",
            StructuralTokenKind.Percent => "Восстановлен плейсхолдер %… по оригиналу",
            _ => "Восстановлена служебная последовательность"
        };

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

        return regex.Replace(value, replacement);
    }

    private enum StructuralTokenKind
    {
        Tag,
        DollarBrace,
        Brace,
        Percent,
        Escape
    }

    private sealed record StructuralToken(
        string Value,
        StructuralTokenKind Kind,
        int Index,
        int Length);

    private sealed record StructureRepairResult(
        string Text,
        bool RequiresReview,
        string ReviewReason);
}
