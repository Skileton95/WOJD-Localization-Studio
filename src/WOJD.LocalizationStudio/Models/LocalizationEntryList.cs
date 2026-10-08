using System.Collections;

namespace WOJD.LocalizationStudio.Models;

/// <summary>
/// Ordered entry storage with constant-time IndexOf for the normal append-only
/// localization document workload. This keeps position display cheap even for
/// documents containing hundreds of thousands of rows.
/// </summary>
public sealed class LocalizationEntryList : IList<LocalizationEntry>, IReadOnlyList<LocalizationEntry>
{
    private readonly List<LocalizationEntry> _items = [];
    private readonly Dictionary<LocalizationEntry, int> _positions = new();

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
        }
    }

    public void Add(LocalizationEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _positions[item] = _items.Count;
        _items.Add(item);
    }

    public void AddRange(IEnumerable<LocalizationEntry> items)
    {
        foreach (var item in items)
            Add(item);
    }

    public void Clear()
    {
        _items.Clear();
        _positions.Clear();
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
    }

    public bool Remove(LocalizationEntry item)
    {
        if (!_positions.TryGetValue(item, out var index))
            return false;

        _items.RemoveAt(index);
        _positions.Remove(item);
        ReindexFrom(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        var removed = _items[index];
        _items.RemoveAt(index);
        _positions.Remove(removed);
        ReindexFrom(index);
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    private void ReindexFrom(int start)
    {
        for (var i = start; i < _items.Count; i++)
            _positions[_items[i]] = i;
    }
}
