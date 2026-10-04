using System.IO;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record BulkTextChange(
    LocalizationEntry Entry,
    string Before,
    string After);

public sealed record BulkUndoOperation(
    string Name,
    IReadOnlyList<BulkTextChange> Changes,
    DateTimeOffset Timestamp);

public static class BulkUndoService
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Stack<BulkUndoOperation>> Undo =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Stack<BulkUndoOperation>> Redo =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Push(
        string filePath,
        string name,
        IEnumerable<BulkTextChange> changes)
    {
        var items = changes.Where(x => !string.Equals(x.Before, x.After, StringComparison.Ordinal)).ToList();
        if (items.Count == 0)
            return;

        lock (Sync)
        {
            var path = Path.GetFullPath(filePath);
            if (!Undo.TryGetValue(path, out var undo))
                Undo[path] = undo = new Stack<BulkUndoOperation>();
            undo.Push(new BulkUndoOperation(name, items, DateTimeOffset.Now));

            if (!Redo.TryGetValue(path, out var redo))
                Redo[path] = redo = new Stack<BulkUndoOperation>();
            redo.Clear();
        }
    }

    public static bool TryUndo(string filePath)
    {
        lock (Sync)
        {
            var path = Path.GetFullPath(filePath);
            if (!Undo.TryGetValue(path, out var undo) || undo.Count == 0)
                return false;

            var operation = undo.Peek();
            if (operation.Changes.Any(x => !string.Equals(x.Entry.Translation, x.After, StringComparison.Ordinal)))
                return false;

            undo.Pop();
            foreach (var change in operation.Changes.Reverse())
                change.Entry.Translation = change.Before;

            if (!Redo.TryGetValue(path, out var redo))
                Redo[path] = redo = new Stack<BulkUndoOperation>();
            redo.Push(operation);
            return true;
        }
    }

    public static bool TryRedo(string filePath)
    {
        lock (Sync)
        {
            var path = Path.GetFullPath(filePath);
            if (!Redo.TryGetValue(path, out var redo) || redo.Count == 0)
                return false;

            var operation = redo.Peek();
            if (operation.Changes.Any(x => !string.Equals(x.Entry.Translation, x.Before, StringComparison.Ordinal)))
                return false;

            redo.Pop();
            foreach (var change in operation.Changes)
                change.Entry.Translation = change.After;

            if (!Undo.TryGetValue(path, out var undo))
                Undo[path] = undo = new Stack<BulkUndoOperation>();
            undo.Push(operation);
            return true;
        }
    }
}
