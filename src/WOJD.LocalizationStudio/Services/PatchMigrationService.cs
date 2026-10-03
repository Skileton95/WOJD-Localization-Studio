using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed class MigrationRow
{
    public bool Include { get; set; }
    public bool Confirmed { get; set; }
    public required EntryLocation Target { get; init; }
    public required EntryLocation Old { get; init; }
    public required string Kind { get; init; }
    public double Score { get; init; }
    public string FilePath => Target.FilePath;
    public string Namespace => Target.Entry.Namespace;
    public string Key => Target.Entry.Key;
    public string OldKey => Old.Entry.Namespace + ":" + Old.Entry.Key;
    public required string Source { get; init; }
    public required string OldSource { get; init; }
    public required string Before { get; init; }
    public required string After { get; init; }
}
public static class PatchMigrationService
{
    public static List<MigrationRow> Preview(IEnumerable<LocalizationDocument> current, IEnumerable<LocalizationDocument> previous, bool overwrite)
    {
        var old = previous.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).ToList();
        var fresh = current.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).ToList();
        var identities = old.GroupBy(l => (l.Entry.Namespace, l.Entry.Key)).ToDictionary(g => g.Key, g => g.ToList());
        var sources = old.GroupBy(l => l.Entry.Original).ToDictionary(g => g.Key, g => g.ToList());
        var currentIdentities = fresh.GroupBy(l => (l.Entry.Namespace, l.Entry.Key)).ToDictionary(g => g.Key, g => g.Count());
        var currentSources = fresh.GroupBy(l => l.Entry.Original).ToDictionary(g => g.Key, g => g.Count());
        var result = new List<MigrationRow>();
        foreach (var target in fresh)
        {
            if (currentIdentities[(target.Entry.Namespace, target.Entry.Key)] != 1) continue;
            if (!overwrite && !string.IsNullOrWhiteSpace(target.Entry.Translation)) continue;
            var exactIdentity = identities.GetValueOrDefault((target.Entry.Namespace, target.Entry.Key)) ?? [];
            EntryLocation? exact = exactIdentity.Count == 1 && exactIdentity[0].Entry.Original == target.Entry.Original ? exactIdentity[0] : null;
            if (exact is not null) { Add(exact, "Точное совпадение", 1, true); continue; }
            if (exactIdentity.Count > 1) continue;
            var sameSource = sources.GetValueOrDefault(target.Entry.Original) ?? [];
            if (sameSource.Count == 1 && currentSources[target.Entry.Original] == 1 && target.Entry.Original.Length > 0) { Add(sameSource[0], "Source совпадает, ключ изменён", 1, true); continue; }
            var candidates = exactIdentity.Concat(old.Where(l => l.Entry.Namespace == target.Entry.Namespace).OrderBy(l => Math.Abs(l.Entry.Original.Length - target.Entry.Original.Length)).Take(500))
                .Distinct().Where(l => !string.IsNullOrWhiteSpace(l.Entry.Translation)).Select(l => (Location: l, Score: Similarity(l.Entry.Original, target.Entry.Original)))
                .Where(l => l.Score >= .6).OrderByDescending(l => l.Score).Take(3);
            foreach (var candidate in candidates) Add(candidate.Location, "Похожий source — проверить вручную", candidate.Score, false);
            void Add(EntryLocation candidate, string kind, double score, bool safe)
            {
                if (string.IsNullOrWhiteSpace(candidate.Entry.Translation) || candidate.Entry.Translation == target.Entry.Translation) return;
                result.Add(new() { Target = target, Old = candidate, Kind = kind, Score = Math.Round(score, 3), Confirmed = safe, Include = safe,
                    Source = target.Entry.Original, OldSource = candidate.Entry.Original, Before = target.Entry.Translation, After = candidate.Entry.Translation });
            }
        }
        return result;
    }
    public static double Similarity(string a, string b)
    {
        if (a == b) return 1; if (a.Length < 2 || b.Length < 2) return 0;
        static HashSet<string> Pairs(string s) => Enumerable.Range(0, s.Length - 1).Select(i => s.Substring(i, 2)).ToHashSet();
        var x = Pairs(a); var y = Pairs(b); return 2.0 * x.Intersect(y).Count() / (x.Count + y.Count);
    }
    public static List<(LocalizationEntry Entry, EntryField Field, string Before, string After)> Changes(IEnumerable<MigrationRow> rows)
    {
        var selected = rows.Where(r => r.Include && r.Confirmed).ToList();
        if (selected.GroupBy(r => r.Target.Entry).Any(g => g.Count() > 1)) throw new InvalidOperationException("Для одной строки выбрано несколько вариантов.");
        if (selected.Any(r => r.Target.Entry.Original != r.Source || r.Target.Entry.Translation != r.Before || r.Old.Entry.Original != r.OldSource || r.Old.Entry.Translation != r.After))
            throw new InvalidOperationException("Просмотр миграции устарел.");
        return selected.Select(r => (r.Target.Entry, EntryField.Translation, r.Before, r.After)).ToList();
    }
}
