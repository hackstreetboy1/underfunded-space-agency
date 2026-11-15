namespace Usa.Game.Comms;

/// <summary>A set that remembers only its most recent <c>capacity</c> items; the oldest is forgotten first.</summary>
internal sealed class BoundedRecentSet<T>
    where T : notnull
{
    private readonly int _capacity;
    private readonly HashSet<T> _items = [];
    private readonly Queue<T> _order = new();

    public BoundedRecentSet(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public int Count => _items.Count;

    /// <returns>False when the item is already remembered.</returns>
    public bool Add(T item)
    {
        if (!_items.Add(item)) return false;
        _order.Enqueue(item);
        if (_order.Count > _capacity) _items.Remove(_order.Dequeue());
        return true;
    }

    public bool Contains(T item) => _items.Contains(item);
}
