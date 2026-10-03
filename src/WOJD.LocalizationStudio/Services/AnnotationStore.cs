using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record RowAnnotation(string FilePath, string Namespace, string Key, string SourceHash, string Note, string Status);
public sealed class AnnotationStore
{
    private readonly string _path = Path.Combine(WorkspaceStateService.StorageDirectory, "annotations.json");
    private Dictionary<string, RowAnnotation> _rows = [];
    public AnnotationStore()
    {
        try { if (File.Exists(_path)) _rows = JsonSerializer.Deserialize<Dictionary<string, RowAnnotation>>(File.ReadAllText(_path)) ?? []; }
        catch (Exception e) { IssueLogService.Record("Заметки: " + e.Message); throw new IOException("Повреждено хранилище заметок; исходный файл сохранён.", e); }
    }
    public static string SourceHash(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    public static string Identity(string file, string ns, string key, string source) => JsonSerializer.Serialize(new[] { Path.GetFullPath(file), ns, key, SourceHash(source) });
    public RowAnnotation? Get(string file, LocalizationEntry row) => _rows.GetValueOrDefault(Identity(file, row.SavedNamespace, row.SavedKey, row.SavedOriginal));
    public void Set(string file, LocalizationEntry row, string note, string status)
    {
        var id = Identity(file, row.SavedNamespace, row.SavedKey, row.SavedOriginal);
        if (note.Length == 0 && status.Length == 0) _rows.Remove(id);
        else _rows[id] = new(file, row.SavedNamespace, row.SavedKey, SourceHash(row.SavedOriginal), note, status);
        Save();
    }
    public List<(LocalizationEntry Entry, RowAnnotation Note, string Id)> Capture(LocalizationDocument document) =>
        document.Entries.Select(e => (Entry: e, Note: Get(document.FilePath, e), Id: Identity(document.FilePath, e.SavedNamespace, e.SavedKey, e.SavedOriginal)))
            .Where(x => x.Note is not null).Select(x => (x.Entry, x.Note!, x.Id)).ToList();
    public void Migrate(string file, List<(LocalizationEntry Entry, RowAnnotation Note, string Id)> notes)
    {
        foreach (var (entry, note, id) in notes)
        {
            var next = Identity(file, entry.SavedNamespace, entry.SavedKey, entry.SavedOriginal);
            _rows.Remove(id); _rows[next] = note with { Namespace = entry.Namespace, Key = entry.Key, SourceHash = SourceHash(entry.Original) };
        }
        if (notes.Count > 0) Save();
    }
    private void Save()
    {
        Directory.CreateDirectory(WorkspaceStateService.StorageDirectory);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_rows, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(_path + ".tmp", _path, true);
    }
}