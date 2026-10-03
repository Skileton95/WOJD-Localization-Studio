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
            e.Field switch { EntryField.Original => "Оригинал", EntryField.Translation => "Перевод", EntryField.Key => "Ключ", _ => "Namespace" }, e.Before, e.After)).ToList();
        EditHistory.Insert(0, new(id, DateTime.UtcNow, CurrentAuthor, label, rows)); _historyEdits[id] = edits;
        while (EditHistory.Count > 2000) { _historyEdits.Remove(EditHistory[^1].Id); EditHistory.RemoveAt(EditHistory.Count - 1); }
    }
    public void RevertHistory(EditHistoryItem record)
    {
        if (!_historyEdits.TryGetValue(record.Id, out var edits)) return;
        if (edits.Any(e => !_entrySessions.ContainsKey(e.Entry) || e.Entry.GetField(e.Field) != e.After))
            throw new InvalidOperationException("Файл закрыт или строка уже изменена после этой записи. Восстановление заблокировано.");
        ApplyBatch(edits.Select(e => (e.Entry, e.Field, e.After, e.Before)), "Восстановление из истории");
    }
}