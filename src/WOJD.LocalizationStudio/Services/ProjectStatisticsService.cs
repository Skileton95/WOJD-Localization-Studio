using System.Text;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record StatisticsRow(string Label, string FilePath, string Namespace, int Total, int Translated, int Untranslated, int Modified, int Qa)
{
    public double Progress => Total == 0 ? 0 : Math.Round(100.0 * Translated / Total, 1);
}
public sealed record ProjectStatistics(int Files, int Rows, int Translated, int Untranslated, int Modified, int Qa,
    int UniqueSources, int UniqueChineseSources, int DuplicateOccurrences, List<StatisticsRow> ByFile, List<StatisticsRow> ByNamespace);
public static class ProjectStatisticsService
{
    private static StatisticsRow Summarize(string label, string file, string ns, IEnumerable<LocalizationEntry> rows)
    {
        var list = rows.ToList();
        return new(label, file, ns, list.Count, list.Count(x => !string.IsNullOrWhiteSpace(x.Translation)),
            list.Count(x => string.IsNullOrWhiteSpace(x.Translation)), list.Count(x => x.Status == TranslationStatus.Modified), list.Count(x => x.HasValidationIssues));
    }
    public static ProjectStatistics Calculate(IEnumerable<LocalizationDocument> documents)
    {
        var docs = documents.Where(d => !d.IsReadOnly).ToList(); var rows = docs.SelectMany(x => x.Entries).ToList();
        var total = Summarize("Проект", "", "", rows);
        var sources = rows.Where(x => x.Original.Length > 0).Select(x => x.Original).ToList();
        var byFile = docs.Select(d => Summarize(System.IO.Path.GetFileName(d.FilePath), d.FilePath, "", d.Entries)).ToList();
        var byNamespace = docs.SelectMany(d => d.Entries.GroupBy(x => x.Namespace).Select(g => Summarize(g.Key.Length == 0 ? "(без Namespace)" : g.Key, d.FilePath, g.Key, g))).ToList();
        return new(docs.Count, total.Total, total.Translated, total.Untranslated, total.Modified, total.Qa, sources.Distinct(StringComparer.Ordinal).Count(),
            sources.Where(x => Regex.IsMatch(x, @"[\u3400-\u9fff]")).Distinct(StringComparer.Ordinal).Count(), sources.Count - sources.Distinct(StringComparer.Ordinal).Count(), byFile, byNamespace);
    }
    public static string ToCsv(ProjectStatistics report)
    {
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var output = new StringBuilder("Scope,File,Namespace,Total,Translated,Untranslated,Modified,QA,Progress\r\n");
        foreach (var item in report.ByFile.Select(row => (Row: row, Scope: "File")).Concat(report.ByNamespace.Select(row => (Row: row, Scope: "Namespace"))))
        {
            var row = item.Row;
            output.AppendLine(string.Join(",", Cell(item.Scope), Cell(row.FilePath), Cell(row.Namespace),
                row.Total, row.Translated, row.Untranslated, row.Modified, row.Qa, row.Progress.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return output.ToString();
    }
}