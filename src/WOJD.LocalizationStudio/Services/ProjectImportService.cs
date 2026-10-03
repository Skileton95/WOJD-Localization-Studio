using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed class ImportRow
{
    public bool Include { get; set; }
    public required string Kind { get; init; }
    public required EntryLocation Incoming { get; init; }
    public LocalizationEntry? Target { get; init; }
    public string FilePath { get; init; } = "";
    public string Namespace => Incoming.Entry.Namespace;
    public string Key => Incoming.Entry.Key;
    public EntryField Field { get; init; }
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string SourceAtPreview { get; init; } = "";
    public bool Allowed { get; init; }
}
public static class ProjectImportService
{
    public static List<ImportRow> Preview(IEnumerable<LocalizationDocument> targets, IEnumerable<LocalizationDocument> incoming, string role, bool overwrite, IReadOnlyDictionary<(string Namespace, string Key), EntryLocation>? choices = null)
    {
        if (role is not ("CN" or "EN" or "RU")) throw new ArgumentException("Неизвестная роль.");
        var current = targets.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).GroupBy(x => (x.Entry.Namespace, x.Entry.Key)).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<ImportRow>();
        foreach (var group in incoming.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).GroupBy(x => (x.Entry.Namespace, x.Entry.Key)))
        {
            current.TryGetValue(group.Key, out var existing); existing ??= [];
            if (existing.Count > 1 && choices?.TryGetValue(group.Key, out var selected) == true && existing.Contains(selected)) existing = [selected];
            foreach (var entry in group)
            {
                var collision = group.Count() != 1 || existing.Count > 1;
                var target = existing.Count == 1 ? existing[0] : null;
                var field = role == "RU" ? EntryField.Translation : EntryField.Original;
                var after = role == "RU" ? entry.Entry.Translation : string.IsNullOrEmpty(entry.Entry.Translation) ? entry.Entry.Original : entry.Entry.Translation;
                var before = target?.Entry.GetField(field) ?? "";
                var kind = collision ? "Коллизия" : target is null ? "Новая" : before == after ? "Совпадает" :
                    role == "RU" && target.Entry.Original != entry.Entry.Original ? "Оригинал изменён" :
                    string.IsNullOrWhiteSpace(after) ? "Пустой импорт" :
                    !string.IsNullOrWhiteSpace(before) && !overwrite ? "Защищена" :
                    role != "RU" && !string.IsNullOrWhiteSpace(target.Entry.Translation) ? "Требует проверки перевода" : "Заполнить";
                result.Add(new() { Kind = kind, Incoming = entry, Target = target?.Entry, FilePath = target?.FilePath ?? "", Field = field, Before = before, After = after,
                    SourceAtPreview = target?.Entry.Original ?? "", Allowed = kind == "Заполнить", Include = kind == "Заполнить" });
            }
        }
        return result;
    }
    public static IEnumerable<(LocalizationEntry Entry, EntryField Field, string Before, string After)> Changes(IEnumerable<ImportRow> rows)
    {
        foreach (var row in rows.Where(r => r.Include && r.Allowed && r.Target is not null))
        {
            if (row.Target!.Namespace != row.Namespace || row.Target.Key != row.Key || row.Target.Original != row.SourceAtPreview || row.Target.GetField(row.Field) != row.Before)
                throw new InvalidOperationException("Просмотр импорта устарел.");
            yield return (row.Target, row.Field, row.Before, row.After);
        }
    }
}
