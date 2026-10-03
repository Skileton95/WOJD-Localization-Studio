using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;

public sealed class PatchSyncRow : ObservableObject
{
    private bool _include;
    public bool Include { get => _include; set => SetProperty(ref _include, value); }
    public required string Kind { get; init; }
    public required string FilePath { get; init; }
    public required string Namespace { get; init; }
    public required string Key { get; init; }
    public LocalizationEntry? Current { get; init; }
    public string OldSource { get; init; } = "";
    public string NewSource { get; init; } = "";
    public string OldTranslation { get; init; } = "";
    public string NewTranslation { get; init; } = "";
    public bool CanTransfer { get; init; }
}
public static class PatchSyncService
{
    public static List<PatchSyncRow> Preview(IEnumerable<LocalizationDocument> current, IEnumerable<LocalizationDocument> previous)
    {
        var oldRows = previous.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e)))
            .GroupBy(x => (x.Entry.Namespace, x.Entry.Key)).ToDictionary(g => g.Key, g => g.ToList());
        var newRows = current.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e)))
            .GroupBy(x => (x.Entry.Namespace, x.Entry.Key)).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<PatchSyncRow>();
        foreach (var identity in oldRows.Keys.Union(newRows.Keys))
        {
            oldRows.TryGetValue(identity, out var old); newRows.TryGetValue(identity, out var fresh);
            old ??= []; fresh ??= [];
            foreach (var loc in fresh.Count > 0 ? fresh : old)
            {
                var before = old.Count == 1 ? old[0].Entry : null;
                var after = fresh.Count > 0 ? loc.Entry : null;
                var collision = old.Count > 1 || fresh.Count > 1;
                var kind = collision ? "Коллизия" : before is null ? "Новая" : after is null ? "Удалена" :
                    before.Original != after.Original ? "Оригинал изменён" :
                    before.Translation == after.Translation ? "Совпадает" :
                    string.IsNullOrWhiteSpace(after.Translation) && !string.IsNullOrWhiteSpace(before.Translation) ? "Можно перенести" : "Перевод изменён";
                var can = !collision && before is not null && after is not null && before.Original == after.Original &&
                    !string.IsNullOrWhiteSpace(before.Translation) && before.Translation != after.Translation;
                result.Add(new() { Kind = kind, FilePath = loc.FilePath, Namespace = identity.Namespace, Key = identity.Key, Current = after,
                    OldSource = before?.Original ?? "", NewSource = after?.Original ?? "",
                    OldTranslation = before?.Translation ?? "", NewTranslation = after?.Translation ?? "",
                    CanTransfer = can, Include = kind == "Можно перенести" });
            }
        }
        return result;
    }
    public static IEnumerable<(LocalizationEntry Entry, EntryField Field, string Before, string After)> Transfers(IEnumerable<PatchSyncRow> rows, bool overwrite)
    {
        foreach (var row in rows.Where(x => x.Include && x.CanTransfer && x.Current is not null))
        {
            if (row.Current!.Original != row.NewSource || row.Current.Translation != row.NewTranslation)
                throw new InvalidOperationException("Строка изменилась после сравнения. Повторите просмотр.");
            if (!overwrite && !string.IsNullOrWhiteSpace(row.Current.Translation)) continue;
            yield return (row.Current, EntryField.Translation, row.NewTranslation, row.OldTranslation);
        }
    }
}