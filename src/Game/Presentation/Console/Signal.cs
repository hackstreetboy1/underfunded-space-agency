using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console;

/// <summary>
/// One-off notifications with no current value (requests, flashes, intents), as opposed to
/// <see cref="ObservableValue{T}"/>. Raised by the owning view model only.
/// </summary>
public sealed class Signal<T>
{
    private readonly SubscriberList<T> _subscribers = new();

    public bool HasSubscribers => !_subscribers.IsEmpty;

    /// <returns>Dispose to unsubscribe.</returns>
    public IDisposable Subscribe(Action<T> handler) => _subscribers.Add(handler);

    internal void Raise(T value) => _subscribers.Notify(value);
}
