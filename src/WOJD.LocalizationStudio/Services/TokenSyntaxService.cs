using System.Text.RegularExpressions;
namespace WOJD.LocalizationStudio.Services;
public sealed record TokenHighlight(int Start, int Length, string Text, bool Mismatch);
public static class TokenSyntaxService
{
    private static readonly Regex Pattern = new(@"\{[A-Za-z0-9_]+(?:[^{}]*)?\}|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.\d+)?[sdifouxXeEgGc]|<\/?[^<>]+?>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static List<TokenHighlight> Analyze(string source, string target)
    {
        var expected = Pattern.Matches(source).Select(x => x.Value).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var matches = Pattern.Matches(target); var actual = matches.Select(x => x.Value).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        return matches.Select(x => new TokenHighlight(x.Index, x.Length, x.Value, !expected.TryGetValue(x.Value, out var count) || count != actual[x.Value])).ToList();
    }
}