using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// Keeps one view per item of an <see cref="ObservableList{T}"/>, in the same order, by applying each granular change
/// (rows keep their bindings across snapshots). Disposing unsubscribes and destroys every view.
/// </summary>
public sealed class ListMirror<TItem, TView> : IDisposable
{
    private readonly ObservableList<TItem> _source;
    private readonly IListViewFactory<TItem, TView> _factory;
    private readonly List<TView> _views = [];
    private IDisposable? _subscription;

    public ListMirror(ObservableList<TItem> source, IListViewFactory<TItem, TView> factory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(factory);
        _source = source;
        _factory = factory;
        Rebuild();
        _subscription = source.Subscribe(Apply);
    }

    /// <summary>The views in item order.</summary>
    public IReadOnlyList<TView> Views => _views;

    public void Dispose()
    {
        if (_subscription is null) return;
        _subscription.Dispose();
        _subscription = null;
        DestroyAll();
    }

    private void Apply(ListChange<TItem> change)
    {
        switch (change.Kind)
        {
            case ListChangeKind.Inserted:
                _views.Insert(change.Index, _factory.Create(change.Item, change.Index));
                break;
            case ListChangeKind.Removed:
                var removed = _views[change.Index];
                _views.RemoveAt(change.Index);
                _factory.Destroy(removed);
                break;
            case ListChangeKind.Replaced:
                _factory.Update(_views[change.Index], change.Item);
                break;
            case ListChangeKind.Moved:
                var moved = _views[change.OldIndex];
                _views.RemoveAt(change.OldIndex);
                _views.Insert(change.Index, moved);
                _factory.Move(moved, change.Index);
                break;
            default:
                Rebuild();
                break;
        }
    }

    private void Rebuild()
    {
        DestroyAll();
        for (var i = 0; i < _source.Count; i++) _views.Add(_factory.Create(_source[i], i));
    }

    private void DestroyAll()
    {
        for (var i = _views.Count - 1; i >= 0; i--) _factory.Destroy(_views[i]);
        _views.Clear();
    }
}
