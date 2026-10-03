using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;

public sealed record EntryLocation(string FilePath, LocalizationEntry Entry);
public sealed record ConsistencyGroup(string Text, List<EntryLocation> Locations, List<string> Variants)
{
    public int Count => Locations.Count;
    public string VariantText => string.Join(" | ", Variants);
}
public sealed record ConsistencyReport(List<ConsistencyGroup> Conflicts, List<ConsistencyGroup> SharedTranslations,
    List<ConsistencyGroup> FrequentTerms);
public static class ConsistencyService
{
    public static ConsistencyReport Analyze(IEnumerable<LocalizationDocument> documents)
    {
        var rows = documents.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).ToList();
        var sources = rows.Where(x => x.Entry.Original.Length > 0).GroupBy(x => x.Entry.Original, StringComparer.Ordinal);
        var conflicts = sources.Select(g => new ConsistencyGroup(g.Key, g.ToList(),
            g.Select(x => x.Entry.Translation).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order().ToList()))
            .Where(g => g.Variants.Count > 1).OrderByDescending(g => g.Count).ToList();
        var shared = rows.Where(x => !string.IsNullOrWhiteSpace(x.Entry.Translation))
            .GroupBy(x => x.Entry.Translation, StringComparer.Ordinal)
            .Select(g => new ConsistencyGroup(g.Key, g.ToList(), g.Select(x => x.Entry.Original).Distinct(StringComparer.Ordinal).ToList()))
            .Where(g => g.Variants.Count > 1).OrderByDescending(g => g.Count).ToList();
        var terms = rows.SelectMany(r => Regex.Matches(r.Entry.Original, @"[\u4e00-\u9fff]{2,}", RegexOptions.None, TimeSpan.FromMilliseconds(100))
                .Select(m => m.Value).Distinct().Select(t => (Term: t, Row: r)))
            .GroupBy(x => x.Term, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => new ConsistencyGroup(g.Key, g.Select(x => x.Row).ToList(),
                g.Select(x => x.Row.Entry.Translation).Distinct(StringComparer.Ordinal).ToList()))
            .OrderByDescending(g => g.Count).Take(500).ToList();
        return new(conflicts, shared, terms);
    }
}