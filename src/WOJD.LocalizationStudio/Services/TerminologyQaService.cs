using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record TermQaException(string FilePath, string Namespace, string Key, string SourceHash, string TranslationHash, string TermId, string Code, string Reason);
public sealed record TermQaIssue(EntryLocation Location, GlossaryTerm Term, string Code, string Detail, string Matched)
{
    public string FilePath => Location.FilePath;
    public int Index => Location.Entry.Index;
    public string Namespace => Location.Entry.Namespace;
    public string Key => Location.Entry.Key;
    public string Source => Location.Entry.Original;
    public string Translation => Location.Entry.Translation;
}
public static class TerminologyQaService
{
    public static bool ContainsForm(string text, string form) => Regex.IsMatch(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(form) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    public static string[] Forms(string value) => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static List<TermQaIssue> Analyze(IEnumerable<LocalizationDocument> documents, IEnumerable<GlossaryTerm> terms, IEnumerable<TermQaException> exceptions)
    {
        var result = new List<TermQaIssue>(); var list = terms.ToList(); var allowed = exceptions.ToList();
        foreach (var doc in documents)
            foreach (var entry in doc.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Translation)) continue;
                foreach (var term in GlossaryService.Applicable(list, entry))
                {
                    var forms = Forms(term.Russian);
                    if (forms.Length > 0 && !forms.Any(f => ContainsForm(entry.Translation, f)))
                        Add(term.Required ? "Обязательный термин" : "Предпочтительный термин", "Нет варианта: " + term.Russian, "");
                    foreach (var forbidden in Forms(term.Forbidden).Where(f => ContainsForm(entry.Translation, f))) Add("Запрещённый термин", "Запрещено: " + forbidden, forbidden);
                    void Add(string code, string detail, string matched)
                    {
                        if (allowed.Any(e => e.FilePath.Equals(doc.FilePath, StringComparison.OrdinalIgnoreCase) && e.Namespace == entry.Namespace && e.Key == entry.Key && e.SourceHash == CollisionService.SourceHash(entry.Original) && e.TranslationHash == CollisionService.SourceHash(entry.Translation) && e.TermId == term.Id && e.Code == code)) return;
                        result.Add(new(new(doc.FilePath, entry), term, code, detail, matched));
                    }
                }
            }
        return result;
    }
    public static TermQaException Except(TermQaIssue issue, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Нужна причина исключения.");
        return new(issue.FilePath, issue.Namespace, issue.Key, CollisionService.SourceHash(issue.Source), CollisionService.SourceHash(issue.Translation), issue.Term.Id, issue.Code, reason.Trim());
    }
}
