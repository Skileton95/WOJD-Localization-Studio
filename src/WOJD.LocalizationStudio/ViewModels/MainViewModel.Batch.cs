using System.IO;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.ViewModels;

public sealed partial class MainViewModel
{
    public async Task EnsureProjectLoadedAsync()
    {
        static IEnumerable<FileNode> Walk(IEnumerable<FileNode> nodes) => nodes.SelectMany(n => n.IsDirectory ? Walk(n.Children) : new[] { n });
        var active = _activeSession;
        foreach (var node in Walk(FileTree).ToArray()) await LoadPathAsync(node.FullPath);
        if (active is not null) ActivateSession(active);
    }
    public void ApplyBatch(IEnumerable<(LocalizationEntry Entry, EntryField Field, string Before, string After)> changes, string label = "Массовая операция")
    {
        var edits = changes.Where(x => x.Before != x.After).Select(x => new TranslationEdit(x.Entry, x.Before, x.After, x.Field)).ToList();
        if (edits.Count == 0) return;
        if (edits.GroupBy(e => (e.Entry, e.Field)).Any(g => g.Count() > 1)) throw new InvalidOperationException("Несколько изменений одного поля строки в одной операции.");
        if (edits.Any(x => !_entrySessions.ContainsKey(x.Entry) || x.Entry.GetField(x.Field) != x.Before))
            throw new InvalidOperationException("Данные изменились после предварительного просмотра. Постройте его заново.");
        var sessions = edits.Select(x => _entrySessions[x.Entry]).Distinct().ToList();
        foreach (var s in sessions) s.HistoryChangeInProgress = true;
        try { foreach (var e in edits) e.Entry.SetField(e.Field, e.After); }
        catch { foreach (var e in edits.AsEnumerable().Reverse()) e.Entry.SetField(e.Field, e.Before); throw; }
        finally { foreach (var s in sessions) s.HistoryChangeInProgress = false; }
        var operation = edits[0] with { Batch = edits };
        foreach (var s in sessions) { s.UndoStack.Push(operation); s.RedoStack.Clear(); }
        RecordHistory(edits, label);
        RefreshNamespaces(); RefreshConsistency(); RaiseHistoryCommandStates();
    }
    private bool ReplayBatch(TranslationEdit operation, bool redo)
    {
        if (operation.Batch is not { } edits) return false;
        if (edits.Any(x => !_entrySessions.ContainsKey(x.Entry)))
            throw new InvalidOperationException("Один из файлов операции закрыт.");
        var sessions = edits.Select(x => _entrySessions[x.Entry]).Distinct().ToList();
        if (sessions.Any(s => (redo ? s.RedoStack : s.UndoStack).Count == 0 ||
            !ReferenceEquals((redo ? s.RedoStack : s.UndoStack).Peek(), operation)) ||
            edits.Any(e => e.Entry.GetField(e.Field) != (redo ? e.Before : e.After)))
            throw new InvalidOperationException("Сначала отмените более поздние изменения в других файлах этой операции.");
        foreach (var s in sessions) s.HistoryChangeInProgress = true;
        try { foreach (var e in redo ? edits : edits.Reverse()) e.Entry.SetField(e.Field, redo ? e.After : e.Before); }
        finally { foreach (var s in sessions) s.HistoryChangeInProgress = false; }
        foreach (var s in sessions)
        {
            (redo ? s.RedoStack : s.UndoStack).Pop();
            (redo ? s.UndoStack : s.RedoStack).Push(operation);
        }
        RefreshNamespaces(); RefreshConsistency(); RaiseHistoryCommandStates(); return true;
    }
}