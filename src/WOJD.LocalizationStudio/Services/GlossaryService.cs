using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed class GlossaryTerm
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Chinese { get; set; } = "";
    public string Russian { get; set; } = "";
    public bool Required { get; set; } = true;
    public string Forbidden { get; set; } = "";
    public string Category { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Comment { get; set; } = "";
}
public static class GlossaryService
{
    public static List<GlossaryTerm> Load(string path) { var terms = ProjectMetadataService.Load<List<GlossaryTerm>>(path, () => []); Validate(terms); return terms; }
    public static void Validate(IReadOnlyList<GlossaryTerm> terms)
    {
        if (terms.Any(t => string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Chinese) || t.Required && string.IsNullOrWhiteSpace(t.Russian)) || terms.Select(t => t.Id).Distinct().Count() != terms.Count)
            throw new ArgumentException("Термин должен иметь CN, уникальный Id и обязательный RU-вариант. Снимите обязательность, если нужен только запрет.");
    }
    public static void Save(string path, List<GlossaryTerm> terms) { Validate(terms); ProjectMetadataService.Save(path, terms); }
    public static IEnumerable<GlossaryTerm> Applicable(IEnumerable<GlossaryTerm> terms, LocalizationEntry entry) =>
        terms.Where(t => (t.Namespace.Length == 0 || t.Namespace == entry.Namespace) && entry.Original.Contains(t.Chinese, StringComparison.Ordinal));
    public static string Hints(IEnumerable<GlossaryTerm> terms, LocalizationEntry entry) =>
        string.Join(" · ", Applicable(terms, entry).Select(t => $"{t.Chinese} → {t.Russian}{(t.Required ? " *" : "")}{(t.Comment.Length > 0 ? " (" + t.Comment + ")" : "")}"));
}
