namespace Usa.Game.Presentation.ViewModels;

/// <summary>
/// Copy-on-write list of callbacks. Subscribing allocates; notifying does not, and handlers may
/// subscribe or unsubscribe while a notification is running (the change applies to the next one).
/// </summary>
internal sealed class SubscriberList<T>
{
    private Action<T>[] _handlers = [];

    public bool IsEmpty => _handlers.Length == 0;

    public IDisposable Add(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, handler);
        var grown = new Action<T>[_handlers.Length + 1];
        _handlers.CopyTo(grown, 0);
        grown[^1] = subscription.Entry;
        _handlers = grown;
        return subscription;
    }

    public void Notify(T value)
    {
        var handlers = _handlers;
        for (var i = 0; i < handlers.Length; i++) handlers[i](value);
    }

    private void Remove(Action<T> entry)
    {
        var index = Array.IndexOf(_handlers, entry);
        if (index < 0) return;
        var shrunk = new Action<T>[_handlers.Length - 1];
        Array.Copy(_handlers, 0, shrunk, 0, index);
        Array.Copy(_handlers, index + 1, shrunk, index, _handlers.Length - index - 1);
        _handlers = shrunk;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action<T> _handler;
        private SubscriberList<T>? _owner;

        public Subscription(SubscriberList<T> owner, Action<T> handler)
        {
            _owner = owner;
            _handler = handler;
            Entry = Invoke;
        }

        public Action<T> Entry { get; }

        public void Dispose()
        {
            if (_owner is null) return;
            _owner.Remove(Entry);
            _owner = null;
        }

        // A handler disposed during a notification must not run later in that same notification.
        private void Invoke(T value)
        {
            if (_owner is not null) _handler(value);
        }
    }
}
