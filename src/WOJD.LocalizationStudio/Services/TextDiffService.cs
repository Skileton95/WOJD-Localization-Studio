namespace WOJD.LocalizationStudio.Services;

public sealed record TextDiffSegment(string Text, bool Changed);

public sealed record TextDiffPair(
    IReadOnlyList<TextDiffSegment> Left,
    IReadOnlyList<TextDiffSegment> Right)
{
    public int ChangedSegments =>
        Left.Count(x => x.Changed) + Right.Count(x => x.Changed);
}

public static class TextDiffService
{
    private const int MaxTokensForLcs = 512;

    public static TextDiffPair Compare(string? left, string? right)
    {
        left ??= string.Empty;
        right ??= string.Empty;

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            IReadOnlyList<TextDiffSegment> unchanged = left.Length == 0
                ? Array.Empty<TextDiffSegment>()
                : new[] { new TextDiffSegment(left, false) };
            return new TextDiffPair(unchanged, unchanged);
        }

        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);

        if (leftTokens.Count > MaxTokensForLcs || rightTokens.Count > MaxTokensForLcs)
            return CompareByPrefixAndSuffix(left, right);

        var lcs = new int[leftTokens.Count + 1, rightTokens.Count + 1];
        for (var i = leftTokens.Count - 1; i >= 0; i--)
        {
            for (var j = rightTokens.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(leftTokens[i], rightTokens[j], StringComparison.Ordinal)
                    ? 1 + lcs[i + 1, j + 1]
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var leftSegments = new List<TextDiffSegment>();
        var rightSegments = new List<TextDiffSegment>();
        var li = 0;
        var ri = 0;

        while (li < leftTokens.Count || ri < rightTokens.Count)
        {
            if (li < leftTokens.Count &&
                ri < rightTokens.Count &&
                string.Equals(leftTokens[li], rightTokens[ri], StringComparison.Ordinal))
            {
                Append(leftSegments, leftTokens[li], changed: false);
                Append(rightSegments, rightTokens[ri], changed: false);
                li++;
                ri++;
                continue;
            }

            if (ri >= rightTokens.Count ||
                (li < leftTokens.Count && lcs[li + 1, ri] >= lcs[li, ri + 1]))
            {
                Append(leftSegments, leftTokens[li], changed: true);
                li++;
            }
            else
            {
                Append(rightSegments, rightTokens[ri], changed: true);
                ri++;
            }
        }

        return new TextDiffPair(leftSegments, rightSegments);
    }

    private static IReadOnlyList<string> Tokenize(string text)
    {
        if (text.Length == 0)
            return Array.Empty<string>();

        var tokens = new List<string>();
        var start = 0;
        var mode = TokenMode.None;

        for (var i = 0; i < text.Length; i++)
        {
            var nextMode = GetMode(text[i]);
            if (nextMode == TokenMode.Cjk || nextMode == TokenMode.Punctuation)
            {
                FlushBuffered(text, tokens, start, i, mode);
                tokens.Add(text[i].ToString());
                start = i + 1;
                mode = TokenMode.None;
                continue;
            }

            if (mode == TokenMode.None)
            {
                start = i;
                mode = nextMode;
                continue;
            }

            if (nextMode != mode)
            {
                tokens.Add(text[start..i]);
                start = i;
                mode = nextMode;
            }
        }

        if (mode != TokenMode.None && start < text.Length)
            tokens.Add(text[start..]);

        return tokens;
    }

    private static void FlushBuffered(
        string text,
        List<string> tokens,
        int start,
        int end,
        TokenMode mode)
    {
        if (mode != TokenMode.None && end > start)
            tokens.Add(text[start..end]);
    }

    private static TokenMode GetMode(char ch)
    {
        if (IsCjk(ch))
            return TokenMode.Cjk;
        if (char.IsWhiteSpace(ch))
            return TokenMode.Space;
        if (char.IsLetterOrDigit(ch) || ch == '_')
            return TokenMode.Word;
        return TokenMode.Punctuation;
    }

    private static bool IsCjk(char ch)
        => (ch >= '\u3400' && ch <= '\u4DBF')
           || (ch >= '\u4E00' && ch <= '\u9FFF')
           || (ch >= '\uF900' && ch <= '\uFAFF');

    private static void Append(List<TextDiffSegment> segments, string text, bool changed)
    {
        if (text.Length == 0)
            return;

        if (segments.Count > 0 && segments[^1].Changed == changed)
        {
            var previous = segments[^1];
            segments[^1] = previous with { Text = previous.Text + text };
            return;
        }

        segments.Add(new TextDiffSegment(text, changed));
    }

    private static TextDiffPair CompareByPrefixAndSuffix(string left, string right)
    {
        var prefix = CommonPrefixLength(left, right);
        var suffix = CommonSuffixLength(left, right, prefix);
        var leftSegments = BuildFallbackSegments(left, prefix, suffix);
        var rightSegments = BuildFallbackSegments(right, prefix, suffix);
        return new TextDiffPair(leftSegments, rightSegments);
    }

    private static IReadOnlyList<TextDiffSegment> BuildFallbackSegments(
        string text,
        int prefix,
        int suffix)
    {
        var segments = new List<TextDiffSegment>(3);
        if (prefix > 0)
            segments.Add(new TextDiffSegment(text[..prefix], false));

        var changedLength = Math.Max(0, text.Length - prefix - suffix);
        if (changedLength > 0)
            segments.Add(new TextDiffSegment(text.Substring(prefix, changedLength), true));

        if (suffix > 0)
            segments.Add(new TextDiffSegment(text[^suffix..], false));

        return segments;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var i = 0;
        while (i < length && left[i] == right[i])
            i++;
        return i;
    }

    private static int CommonSuffixLength(string left, string right, int prefix)
    {
        var max = Math.Min(left.Length, right.Length) - prefix;
        var i = 0;
        while (i < max && left[left.Length - 1 - i] == right[right.Length - 1 - i])
            i++;
        return i;
    }

    private enum TokenMode
    {
        None,
        Word,
        Space,
        Punctuation,
        Cjk
    }
}
