using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed class MarkerRule { public string Name { get; set; } = ""; public string Pattern { get; set; } = ""; }
public sealed class WojdQaProfile
{
    public int Schema { get; set; } = 1;
    public bool ChineseLeft { get; set; } = true;
    public bool EmptyTranslation { get; set; } = true;
    public bool SuspiciousLength { get; set; } = true;
    public bool TagBalance { get; set; } = true;
    public bool Controls { get; set; } = true;
    public bool UnchangedTarget { get; set; } = true;
    public double MinRatio { get; set; } = .15;
    public double MaxRatio { get; set; } = 4;
    public List<MarkerRule> Markers { get; set; } = [new() { Name = "Экранированные управляющие символы", Pattern = @"\\[nrt]" }];
}
public sealed record WojdQaIssue(EntryLocation Location, string Severity, string Code, string Detail)
{
    public string FilePath => Location.FilePath;
    public int Index => Location.Entry.Index;
    public string Namespace => Location.Entry.Namespace;
    public string Key => Location.Entry.Key;
    public string Source => Location.Entry.Original;
    public string Translation => Location.Entry.Translation;
}
public static class WojdQaService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    public static void Validate(WojdQaProfile profile)
    {
        if (profile.Schema != 1 || !double.IsFinite(profile.MinRatio) || !double.IsFinite(profile.MaxRatio) || profile.MinRatio <= 0 || profile.MaxRatio <= profile.MinRatio || profile.Markers is null) throw new ArgumentException("Проверьте границы длины и версию профиля.");
        foreach (var rule in profile.Markers) { if (rule.Name.Length == 0 || rule.Pattern.Length == 0) throw new ArgumentException("Маркеру нужны название и Regex."); _ = new Regex(rule.Pattern, RegexOptions.CultureInvariant, Timeout); }
    }
    public static List<WojdQaIssue> Analyze(IEnumerable<LocalizationDocument> documents, WojdQaProfile profile)
    {
        Validate(profile); var result = new List<WojdQaIssue>();
        var markers = profile.Markers.Select(r => (r.Name, Matcher: new Regex(r.Pattern, RegexOptions.CultureInvariant, Timeout))).ToList();
        foreach (var doc in documents)
            foreach (var entry in doc.Entries)
            {
                var source = entry.Original; var text = entry.Translation;
                void Add(string severity, string code, string detail) => result.Add(new(new(doc.FilePath, entry), severity, code, detail));
                if (string.IsNullOrWhiteSpace(text)) { if (profile.EmptyTranslation && !string.IsNullOrWhiteSpace(source)) Add("Предупреждение", "Пустой перевод", "Строка ещё не переведена."); continue; }
                var standard = TranslationValidator.Validate(source, text);
                if (standard.IssueCount > 0) Add("Ошибка", "Базовый QA", standard.Summary);
                foreach (var (name, matcher) in markers)
                    if (!Signature(source, matcher).SequenceEqual(Signature(text, matcher))) Add("Ошибка", "Маркер: " + name, "Набор и количество маркеров не совпадают.");
                var tags = new Regex(@"</?[^<>]+?>|</>", RegexOptions.CultureInvariant, Timeout);
                if (!Signature(source, tags).SequenceEqual(Signature(text, tags))) Add("Ошибка", "Rich text", "Теги и атрибуты не совпадают.");
                if (profile.TagBalance && Balanced(source, tags) && !Balanced(text, tags)) Add("Ошибка", "Вложенность тегов", "Перевод нарушает порядок закрытия.");
                if (profile.Controls && !Signature(source, new Regex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]", RegexOptions.None, Timeout)).SequenceEqual(Signature(text, new Regex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]", RegexOptions.None, Timeout))))
                    Add("Ошибка", "Управляющие символы", "Управляющие символы отличаются.");
                if (profile.ChineseLeft && Regex.IsMatch(text, @"[\u3400-\u9FFF\uF900-\uFAFF]", RegexOptions.None, Timeout)) Add("Предупреждение", "Китайский остаток", "Проверьте оставленный китайский текст.");
                if (profile.UnchangedTarget && text == source) Add("Предупреждение", "Совпадает с source", "Возможно, перевод не выполнен; имена могут совпадать намеренно.");
                if (profile.SuspiciousLength && source.Length >= 12 && ((double)text.Length / source.Length < profile.MinRatio || (double)text.Length / source.Length > profile.MaxRatio))
                    Add("Предупреждение", "Соотношение длин", $"Source: {source.Length}, перевод: {text.Length}.");
            }
        return result;
    }
    private static string[] Signature(string text, Regex matcher) => matcher.Matches(text).Select(m => m.Value).OrderBy(s => s, StringComparer.Ordinal).ToArray();
    private static bool Balanced(string text, Regex matcher)
    {
        var stack = new Stack<string>();
        foreach (Match match in matcher.Matches(text))
        {
            var token = match.Value; if (token.EndsWith("/>")) continue;
            var name = Regex.Match(token, @"^</?([^\s/>]+)", RegexOptions.None, Timeout).Groups[1].Value;
            if (token.StartsWith("</")) { if (stack.Count == 0) return false; var open = stack.Pop(); if (name.Length > 0 && name != open) return false; }
            else stack.Push(name);
        }
        return stack.Count == 0;
    }
}
