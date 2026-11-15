using System.Collections;

namespace Usa.Game.Presentation.ViewModels;

/// <summary>
/// A list that reports each change as a <see cref="ListChange{T}"/> so UI can insert, remove or move one row
/// instead of rebuilding. Replacing an item with an equal one (per the comparer) raises nothing.
/// </summary>
public sealed class ObservableList<T> : IReadOnlyList<T>
{
    private readonly List<T> _items;
    private readonly IEqualityComparer<T> _comparer;
    private readonly SubscriberList<ListChange<T>> _subscribers = new();

    public ObservableList(IEqualityComparer<T>? comparer = null)
        : this([], comparer)
    {
    }

    public ObservableList(IEnumerable<T> items, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public int Count => _items.Count;

    public T this[int index]
    {
        get => _items[index];
        set => Replace(index, value);
    }

    public IDisposable Subscribe(Action<ListChange<T>> onChanged) => _subscribers.Add(onChanged);

    public void Add(T item) => Insert(_items.Count, item);

    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        _subscribers.Notify(ListChange.Inserted(index, item));
    }

    public bool Remove(T item)
    {
        var index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        _subscribers.Notify(ListChange.Removed(index, item));
    }

    /// <returns>True when the item changed.</returns>
    public bool Replace(int index, T item)
    {
        var old = _items[index];
        if (_comparer.Equals(old, item)) return false;
        _items[index] = item;
        _subscribers.Notify(ListChange.Replaced(index, old, item));
        return true;
    }

    public void Move(int oldIndex, int newIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, _items.Count);
        var item = _items[oldIndex];
        if (oldIndex == newIndex) return;
        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, item);
        _subscribers.Notify(ListChange.Moved(oldIndex, newIndex, item));
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        _subscribers.Notify(ListChange.Reset<T>());
    }

    /// <summary>Replaces the whole content with one <see cref="ListChangeKind.Reset"/> notification.</summary>
    public void ResetTo(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        _items.AddRange(items);
        _subscribers.Notify(ListChange.Reset<T>());
    }

    public int IndexOf(T item)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_comparer.Equals(_items[i], item)) return i;
        }
        return -1;
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <summary>Struct enumerator so <c>foreach</c> over the concrete type does not allocate.</summary>
    public List<T>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
