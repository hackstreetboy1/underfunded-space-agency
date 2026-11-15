using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Hosting;

/// <summary>
/// An observable value whose subscribers are notified one by one in isolation: a handler that throws is reported to the
/// error callback and the rest still run. For app-wide signals (the freeze) where one broken listener must not leave the
/// others in the wrong state. Handlers may subscribe or unsubscribe during a notification (it applies to the next one).
/// </summary>
internal sealed class IsolatedObservable<T> : IReadOnlyObservable<T>
{
    private readonly EqualityComparer<T> _comparer = EqualityComparer<T>.Default;
    private readonly Action<Exception> _onError;
    private Subscription[] _subscriptions = [];

    public IsolatedObservable(T initial, Action<Exception> onError)
    {
        Value = initial;
        _onError = onError;
    }

    public T Value { get; private set; }

    public void Set(T value)
    {
        if (_comparer.Equals(Value, value)) return;
        Value = value;
        var subscriptions = _subscriptions;
        for (var i = 0; i < subscriptions.Length; i++) subscriptions[i].Notify(value);
    }

    public IDisposable Subscribe(Action<T> onChanged, bool invokeImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        var subscription = new Subscription(this, onChanged);
        _subscriptions = [.. _subscriptions, subscription];
        if (invokeImmediately) onChanged(Value);
        return subscription;
    }

    private void Remove(Subscription subscription)
    {
        var index = Array.IndexOf(_subscriptions, subscription);
        if (index < 0) return;
        var shrunk = new Subscription[_subscriptions.Length - 1];
        Array.Copy(_subscriptions, 0, shrunk, 0, index);
        Array.Copy(_subscriptions, index + 1, shrunk, index, _subscriptions.Length - index - 1);
        _subscriptions = shrunk;
    }

    private sealed class Subscription(IsolatedObservable<T> owner, Action<T> handler) : IDisposable
    {
        private IsolatedObservable<T>? _owner = owner;

        public void Notify(T value)
        {
            // Disposed during this notification: it must not run later in it.
            if (_owner is not { } owner) return;
            try
            {
                handler(value);
            }
            catch (Exception ex)
            {
                owner._onError(ex);
            }
        }

        public void Dispose()
        {
            _owner?.Remove(this);
            _owner = null;
        }
    }
}
