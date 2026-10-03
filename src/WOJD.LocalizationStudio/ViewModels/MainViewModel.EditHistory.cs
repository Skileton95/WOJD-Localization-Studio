using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public BulkObservableCollection<EditHistoryItem> EditHistory { get; } = new();
    private readonly Dictionary<Guid, IReadOnlyList<TranslationEdit>> _historyEdits = [];
    private void RecordHistory(IReadOnlyList<TranslationEdit> edits, string label)
    {
        var id = Guid.NewGuid();
        var rows = edits.Select(e => new EditHistoryRow(_entrySessions[e.Entry].Document.FilePath, e.Entry.Namespace, e.Entry.Key,
            e.Field switch { EntryField.Original => "Оригинал", EntryField.Translation => "Перевод", EntryField.Key => "Ключ", _ => "Namespace" }, e.Before, e.After, WOJD.LocalizationStudio.Services.CollisionService.SourceHash(e.Entry.Original))).ToList();
        var item = new EditHistoryItem(id, DateTime.UtcNow, CurrentAuthor, label, rows); EditHistory.Insert(0, item); _historyEdits[id] = edits;
        WOJD.LocalizationStudio.Services.EditHistoryJournal.Append(ProjectDataDirectory, item);
        while (EditHistory.Count > 2000) { _historyEdits.Remove(EditHistory[^1].Id); EditHistory.RemoveAt(EditHistory.Count - 1); }
    }
    private string? _historyDirectory;
    public void RestoreEditHistory()
    {
        if (_historyDirectory == ProjectDataDirectory) return;
        _historyDirectory = ProjectDataDirectory; EditHistory.Clear(); _historyEdits.Clear();
        foreach (var item in WOJD.LocalizationStudio.Services.EditHistoryJournal.Read(ProjectDataDirectory)) EditHistory.Add(item);
    }
    public void RevertHistory(EditHistoryItem record)
    {
        if (!_historyEdits.TryGetValue(record.Id, out var edits))
        {
            var restored = new List<TranslationEdit>();
            foreach (var row in record.Rows)
            {
                if (row.SourceHash is null) throw new InvalidOperationException("Старая запись без хэша source доступна только для просмотра.");
                if (!_sessions.TryGetValue(System.IO.Path.GetFullPath(row.FilePath), out var session)) throw new InvalidOperationException("Сначала откройте все файлы операции.");
                var field = row.Field switch { "Оригинал" => EntryField.Original, "Перевод" => EntryField.Translation, "Ключ" => EntryField.Key, "Namespace" => EntryField.Namespace, _ => throw new InvalidOperationException("Неизвестное поле истории.") };
                var matches = session.Document.Entries.Where(e => e.Namespace == row.Namespace && e.Key == row.Key && WOJD.LocalizationStudio.Services.CollisionService.SourceHash(e.Original) == row.SourceHash && e.GetField(field) == row.After).Take(2).ToList();
                if (matches.Count != 1) throw new InvalidOperationException("Строка изменилась/неоднозначна; восстановление заблокировано.");
                restored.Add(new(matches[0], row.Before, row.After, field));
            }
            ApplyBatch(restored.Select(e => (e.Entry, e.Field, e.After, e.Before)), "Восстановление сохранённой истории"); return;
        }
        if (edits.Any(e => !_entrySessions.ContainsKey(e.Entry) || e.Entry.GetField(e.Field) != e.After))
            throw new InvalidOperationException("Файл закрыт или строка уже изменена после этой записи. Восстановление заблокировано.");
        ApplyBatch(edits.Select(e => (e.Entry, e.Field, e.After, e.Before)), "Восстановление из истории");
    }
}