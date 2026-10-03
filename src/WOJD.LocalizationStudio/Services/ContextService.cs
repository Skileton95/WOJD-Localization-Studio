using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record ContextRow(string Kind, string FilePath, int Index, string Namespace, string Key, string Source, string Translation, EntryLocation? Location);
public static class ContextService
{
    public static List<ContextRow> Analyze(IReadOnlyList<LocalizationDocument> documents, string activePath, LocalizationEntry selected)
    {
        var rows = new List<ContextRow>(); var active = documents.FirstOrDefault(d => d.FilePath == activePath);
        if (active is not null) rows.AddRange(active.Entries.Skip(Math.Max(0, selected.Index - 3)).Take(5).Select(e => Row("Соседняя строка", active.FilePath, e)));
        var all = documents.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).ToList();
        rows.AddRange(all.Where(l => !ReferenceEquals(l.Entry, selected) && selected.Original.Length > 0 && l.Entry.Original == selected.Original).Take(500).Select(l => Row("Тот же source", l.FilePath, l.Entry)));
        var prefix = selected.Key.Split(['_', '.', '/', '-'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (prefix.Length >= 3) rows.AddRange(all.Where(l => !ReferenceEquals(l.Entry, selected) && l.Entry.Namespace == selected.Namespace && l.Entry.Key.StartsWith(prefix, StringComparison.Ordinal) && l.Entry.Original != selected.Original).Take(100).Select(l => Row("Возможная связь по ключу", l.FilePath, l.Entry)));
        return rows;
    }
    private static ContextRow Row(string kind, string file, LocalizationEntry entry) => new(kind, file, entry.Index, entry.Namespace, entry.Key, entry.Original, entry.Translation, new(file, entry));
    public static List<ContextRow> Historical(string dataDirectory, string path, LocalizationEntry entry)
    {
        var rows = new List<ContextRow>();
        var baseline = ProjectMetadataService.Load<ReleaseBaseline?>(Path.Combine(dataDirectory, "release-baseline.json"), () => null);
        rows.AddRange((baseline?.Rows ?? []).Where(r => r.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase) && r.Namespace == entry.Namespace && r.Key == entry.Key)
            .Select(r => new ContextRow("База пакета " + baseline!.Version, r.FilePath, r.Index, r.Namespace, r.Key, r.Source, r.Translation, null)));
        if (!Directory.Exists(dataDirectory)) return rows;
        foreach (var file in Directory.EnumerateFiles(dataDirectory, "migration-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(20))
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var item in json.RootElement.EnumerateArray())
                    if (item.GetProperty("FilePath").GetString()?.Equals(path, StringComparison.OrdinalIgnoreCase) == true && item.GetProperty("Namespace").GetString() == entry.Namespace && item.GetProperty("Key").GetString() == entry.Key)
                        rows.Add(new("Миграция: " + Path.GetFileName(file), path, entry.Index, entry.Namespace, entry.Key, item.GetProperty("OldSource").GetString() ?? "", item.GetProperty("After").GetString() ?? "", null));
            } catch (Exception e) { IssueLogService.Record("Контекст миграции: " + e.Message); }
        return rows;
    }
}
