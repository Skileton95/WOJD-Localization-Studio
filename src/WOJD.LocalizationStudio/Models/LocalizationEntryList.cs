using System.Collections;

namespace WOJD.LocalizationStudio.Models;

/// <summary>
/// Ordered entry storage with constant-time IndexOf for the normal append-only
/// localization document workload. Optional callbacks let document-level indexes
/// grow while a file is being parsed on its background loading thread.
/// </summary>
public sealed class LocalizationEntryList : IList<LocalizationEntry>, IReadOnlyList<LocalizationEntry>
{
    private readonly List<LocalizationEntry> _items = [];
    private readonly Dictionary<LocalizationEntry, int> _positions = new();
    private readonly Action<LocalizationEntry>? _onAppend;
    private readonly Action? _onStructureChanged;

    public LocalizationEntryList(
        Action<LocalizationEntry>? onAppend = null,
        Action? onStructureChanged = null)
    {
        _onAppend = onAppend;
        _onStructureChanged = onStructureChanged;
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public LocalizationEntry this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _items[index] = value;
            ReindexFrom(index);
            _onStructureChanged?.Invoke();
        }
    }

    public void Add(LocalizationEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _positions[item] = _items.Count;
        _items.Add(item);
        _onAppend?.Invoke(item);
    }

    public void AddRange(IEnumerable<LocalizationEntry> items)
    {
        foreach (var item in items)
            Add(item);
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;

        _items.Clear();
        _positions.Clear();
        _onStructureChanged?.Invoke();
    }

    public bool Contains(LocalizationEntry item)
        => _positions.ContainsKey(item);

    public void CopyTo(LocalizationEntry[] array, int arrayIndex)
        => _items.CopyTo(array, arrayIndex);

    public IEnumerator<LocalizationEntry> GetEnumerator()
        => _items.GetEnumerator();

    public int IndexOf(LocalizationEntry item)
        => _positions.TryGetValue(item, out var index) ? index : -1;

    public void Insert(int index, LocalizationEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        ReindexFrom(index);
        _onStructureChanged?.Invoke();
    }

    public bool Remove(LocalizationEntry item)
    {
        if (!_positions.TryGetValue(item, out var index))
            return false;

        _items.RemoveAt(index);
        _positions.Remove(item);
        ReindexFrom(index);
        _onStructureChanged?.Invoke();
        return true;
    }

    public void RemoveAt(int index)
    {
        var removed = _items[index];
        _items.RemoveAt(index);
        _positions.Remove(removed);
        ReindexFrom(index);
        _onStructureChanged?.Invoke();
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    private void ReindexFrom(int start)
    {
        for (var i = start; i < _items.Count; i++)
            _positions[_items[i]] = i;
    }
}
