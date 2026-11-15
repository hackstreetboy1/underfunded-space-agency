using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// The subscriptions one view holds on its view model. <see cref="Release"/> drops them newest first and leaves the scope
/// reusable, so a view can be rebound to another view model.
/// </summary>
public sealed class BindingScope : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    public int Count => _owned.Count;

    public void Add(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _owned.Add(subscription);
    }

    /// <summary>Applies the current value at once, then every published change.</summary>
    public void Observe<T>(IReadOnlyObservable<T> source, Action<T> apply)
    {
        ArgumentNullException.ThrowIfNull(source);
        Add(source.Subscribe(apply));
    }

    public void On<T>(Signal<T> signal, Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(signal);
        Add(signal.Subscribe(handler));
    }

    /// <summary>Mirrors <paramref name="source"/> into views until released.</summary>
    public ListMirror<TItem, TView> Mirror<TItem, TView>(ObservableList<TItem> source, IListViewFactory<TItem, TView> factory)
    {
        var mirror = new ListMirror<TItem, TView>(source, factory);
        Add(mirror);
        return mirror;
    }

    public void Release()
    {
        for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }

    public void Dispose() => Release();
}
