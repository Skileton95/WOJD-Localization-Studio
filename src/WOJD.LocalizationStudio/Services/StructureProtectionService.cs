using System.Text.RegularExpressions;

namespace WOJD.LocalizationStudio.Services;

public enum ProtectedStructureTokenKind
{
    Tag,
    Placeholder
}

public sealed record ProtectedStructureToken(
    ProtectedStructureTokenKind Kind,
    string Value,
    int Index,
    int Length);

public sealed record StructureProtectionStatus(
    int SourceTagCount,
    int TargetTagCount,
    int SourcePlaceholderCount,
    int TargetPlaceholderCount,
    int SourceNewLineCount,
    int TargetNewLineCount,
    StructuralQaResult StructuralResult)
{
    public bool HasTagIssues
        => StructuralResult.MissingTags.Count > 0 ||
           StructuralResult.ExtraTags.Count > 0 ||
           StructuralResult.TagOrderMismatch;

    public bool HasPlaceholderIssues
        => StructuralResult.MissingPlaceholders.Count > 0 ||
           StructuralResult.ExtraPlaceholders.Count > 0;

    public bool HasNewLineIssues
        => SourceNewLineCount != TargetNewLineCount;

    public int StructuralIssueScore
        => StructuralResult.MissingTags.Count +
           StructuralResult.ExtraTags.Count +
           StructuralResult.MissingPlaceholders.Count +
           StructuralResult.ExtraPlaceholders.Count +
           (StructuralResult.TagOrderMismatch ? 1 : 0);

    public string CompactSummary
        => $"Теги {TargetTagCount}/{SourceTagCount} {(HasTagIssues ? "✕" : "✓")}   " +
           $"Плейсхолдеры {TargetPlaceholderCount}/{SourcePlaceholderCount} {(HasPlaceholderIssues ? "✕" : "✓")}   " +
           $"Переносы {TargetNewLineCount}/{SourceNewLineCount} {(HasNewLineIssues ? "✕" : "✓")}";
}

public sealed record StructureRestoreResult(
    bool Changed,
    bool CanRestore,
    string Text,
    string Message);

public static class StructureProtectionService
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

    public static StructureProtectionStatus Analyze(
        string original,
        string translation)
    {
        original ??= string.Empty;
        translation ??= string.Empty;

        var sourceTokens = ParseTokens(original);
        var targetTokens = ParseTokens(translation);

        return new StructureProtectionStatus(
            sourceTokens.Count(x => x.Kind == ProtectedStructureTokenKind.Tag),
            targetTokens.Count(x => x.Kind == ProtectedStructureTokenKind.Tag),
            sourceTokens.Count(x => x.Kind == ProtectedStructureTokenKind.Placeholder),
            targetTokens.Count(x => x.Kind == ProtectedStructureTokenKind.Placeholder),
            CountNewLines(original),
            CountNewLines(translation),
            StructuralQaService.Analyze(original, translation));
    }

    public static bool WouldWorsenStructure(
        string original,
        string currentText,
        string proposedText)
    {
        var current = Analyze(original, currentText);
        var proposed = Analyze(original, proposedText);

        return proposed.StructuralIssueScore > current.StructuralIssueScore;
    }

    public static StructureRestoreResult RestoreStructure(
        string original,
        string translation)
    {
        original ??= string.Empty;
        translation ??= string.Empty;

        var qa = StructuralQaService.Analyze(original, translation);

        if (!qa.HasIssues)
        {
            return new StructureRestoreResult(
                false,
                true,
                translation,
                "Структура уже совпадает с оригиналом.");
        }

        var sourceTokens = ParseTokens(original);
        var targetTokens = ParseTokens(translation);

        // Самый частый случай после перевода: один тег/плейсхолдер был
        // изменён (например RTP_Default -> RTP_Defaut или {0} -> {O}).
        if (TryReplaceSingleWrongToken(
                translation,
                sourceTokens,
                targetTokens,
                qa,
                out var replaced))
        {
            return Success(replaced, "Повреждённый тег или плейсхолдер восстановлен по оригиналу.");
        }

        // Один лишний токен можно безопасно удалить: русский текст при этом
        // не меняется, а структура становится ближе к Original.
        if (TryRemoveSingleExtraToken(
                translation,
                targetTokens,
                qa,
                out var removed))
        {
            return Success(removed, "Лишний тег или плейсхолдер удалён по структуре оригинала.");
        }

        // Потерянный токен восстанавливаем автоматически только на границе,
        // где его позиция доказуема и не зависит от русской грамматики.
        if (TryInsertSingleBoundaryToken(
                original,
                translation,
                sourceTokens,
                qa,
                out var inserted))
        {
            return Success(inserted, "Потерянный граничный тег или плейсхолдер восстановлен по оригиналу.");
        }

        // Полностью потерянная внешняя пара тегов: <...>текст</>.
        if (TryRestoreOuterTagPair(
                original,
                translation,
                sourceTokens,
                targetTokens,
                qa,
                out var wrapped))
        {
            return Success(wrapped, "Внешняя пара тегов восстановлена по оригиналу.");
        }

        return new StructureRestoreResult(
            false,
            false,
            translation,
            "Позицию повреждённых токенов нельзя определить безопасно. Русский текст не изменён.\n\n" +
            qa.Summary);
    }

    private static StructureRestoreResult Success(
        string text,
        string message)
        => new(
            true,
            true,
            text,
            message);

    private static bool TryReplaceSingleWrongToken(
        string translation,
        IReadOnlyList<ProtectedStructureToken> sourceTokens,
        IReadOnlyList<ProtectedStructureToken> targetTokens,
        StructuralQaResult qa,
        out string result)
    {
        result = translation;

        string? missing = null;
        string? extra = null;
        ProtectedStructureTokenKind? kind = null;

        if (qa.MissingTags.Count == 1 &&
            qa.ExtraTags.Count == 1 &&
            qa.MissingPlaceholders.Count == 0 &&
            qa.ExtraPlaceholders.Count == 0)
        {
            missing = qa.MissingTags[0];
            extra = qa.ExtraTags[0];
            kind = ProtectedStructureTokenKind.Tag;
        }
        else if (qa.MissingPlaceholders.Count == 1 &&
                 qa.ExtraPlaceholders.Count == 1 &&
                 qa.MissingTags.Count == 0 &&
                 qa.ExtraTags.Count == 0 &&
                 !qa.TagOrderMismatch)
        {
            missing = qa.MissingPlaceholders[0];
            extra = qa.ExtraPlaceholders[0];
            kind = ProtectedStructureTokenKind.Placeholder;
        }

        if (missing is null || extra is null || kind is null)
            return false;

        var target = targetTokens.FirstOrDefault(
            x => x.Kind == kind &&
                 string.Equals(x.Value, extra, StringComparison.Ordinal));

        if (target is null)
            return false;

        // Не заменяем токен, если такой тип вообще отсутствует в Original.
        if (!sourceTokens.Any(x =>
                x.Kind == kind &&
                string.Equals(x.Value, missing, StringComparison.Ordinal)))
        {
            return false;
        }

        result = ReplaceRange(
            translation,
            target.Index,
            target.Length,
            missing);

        return !StructuralQaService.Analyze(
            string.Concat(), // проверка ниже выполняется вызывающим кодом не нужна
            string.Concat()).HasIssues || result != translation;
    }

    private static bool TryRemoveSingleExtraToken(
        string translation,
        IReadOnlyList<ProtectedStructureToken> targetTokens,
        StructuralQaResult qa,
        out string result)
    {
        result = translation;

        string? extra = null;
        ProtectedStructureTokenKind? kind = null;

        if (qa.ExtraTags.Count == 1 &&
            qa.MissingTags.Count == 0 &&
            qa.ExtraPlaceholders.Count == 0 &&
            qa.MissingPlaceholders.Count == 0 &&
            !qa.TagOrderMismatch)
        {
            extra = qa.ExtraTags[0];
            kind = ProtectedStructureTokenKind.Tag;
        }
        else if (qa.ExtraPlaceholders.Count == 1 &&
                 qa.MissingPlaceholders.Count == 0 &&
                 qa.ExtraTags.Count == 0 &&
                 qa.MissingTags.Count == 0 &&
                 !qa.TagOrderMismatch)
        {
            extra = qa.ExtraPlaceholders[0];
            kind = ProtectedStructureTokenKind.Placeholder;
        }

        if (extra is null || kind is null)
            return false;

        var target = targetTokens.FirstOrDefault(
            x => x.Kind == kind &&
                 string.Equals(x.Value, extra, StringComparison.Ordinal));

        if (target is null)
            return false;

        result = ReplaceRange(
            translation,
            target.Index,
            target.Length,
            string.Empty);

        return true;
    }

    private static bool TryInsertSingleBoundaryToken(
        string original,
        string translation,
        IReadOnlyList<ProtectedStructureToken> sourceTokens,
        StructuralQaResult qa,
        out string result)
    {
        result = translation;

        string? missing = null;
        ProtectedStructureTokenKind? kind = null;

        if (qa.MissingTags.Count == 1 &&
            qa.ExtraTags.Count == 0 &&
            qa.MissingPlaceholders.Count == 0 &&
            qa.ExtraPlaceholders.Count == 0 &&
            !qa.TagOrderMismatch)
        {
            missing = qa.MissingTags[0];
            kind = ProtectedStructureTokenKind.Tag;
        }
        else if (qa.MissingPlaceholders.Count == 1 &&
                 qa.ExtraPlaceholders.Count == 0 &&
                 qa.MissingTags.Count == 0 &&
                 qa.ExtraTags.Count == 0 &&
                 !qa.TagOrderMismatch)
        {
            missing = qa.MissingPlaceholders[0];
            kind = ProtectedStructureTokenKind.Placeholder;
        }

        if (missing is null || kind is null)
            return false;

        var candidates = sourceTokens
            .Where(x =>
                x.Kind == kind &&
                string.Equals(x.Value, missing, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count != 1)
            return false;

        var token = candidates[0];

        if (token.Index == 0)
        {
            result = token.Value + translation;
            return true;
        }

        if (token.Index + token.Length == original.Length)
        {
            result = translation + token.Value;
            return true;
        }

        return false;
    }

    private static bool TryRestoreOuterTagPair(
        string original,
        string translation,
        IReadOnlyList<ProtectedStructureToken> sourceTokens,
        IReadOnlyList<ProtectedStructureToken> targetTokens,
        StructuralQaResult qa,
        out string result)
    {
        result = translation;

        if (qa.MissingTags.Count != 2 ||
            qa.ExtraTags.Count != 0 ||
            qa.MissingPlaceholders.Count != 0 ||
            qa.ExtraPlaceholders.Count != 0 ||
            targetTokens.Any(x => x.Kind == ProtectedStructureTokenKind.Tag))
        {
            return false;
        }

        var sourceTags = sourceTokens
            .Where(x => x.Kind == ProtectedStructureTokenKind.Tag)
            .ToList();

        if (sourceTags.Count != 2)
            return false;

        var first = sourceTags[0];
        var last = sourceTags[1];

        if (first.Index != 0 ||
            last.Index + last.Length != original.Length)
        {
            return false;
        }

        result = first.Value + translation + last.Value;
        return true;
    }

    private static IReadOnlyList<ProtectedStructureToken> ParseTokens(string text)
    {
        var result = new List<ProtectedStructureToken>();

        foreach (Match match in TagRegex.Matches(text))
        {
            result.Add(
                new ProtectedStructureToken(
                    ProtectedStructureTokenKind.Tag,
                    match.Value,
                    match.Index,
                    match.Length));
        }

        foreach (Match match in PlaceholderRegex.Matches(text))
        {
            result.Add(
                new ProtectedStructureToken(
                    ProtectedStructureTokenKind.Placeholder,
                    match.Value,
                    match.Index,
                    match.Length));
        }

        return result
            .OrderBy(x => x.Index)
            .ThenBy(x => x.Length)
            .ToList();
    }

    private static string ReplaceRange(
        string text,
        int index,
        int length,
        string replacement)
        => text.Remove(index, length).Insert(index, replacement);

    private static int CountNewLines(string text)
    {
        var count = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }

        return count;
    }
}
