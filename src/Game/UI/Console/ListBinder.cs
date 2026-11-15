using Godot;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console;

/// <summary>
/// Instantiates one row scene per list item inside a dedicated container (it must hold no other children) and keeps the
/// children in item order. Removed rows leave the tree at once, which unbinds them, and are then freed.
/// </summary>
public sealed class ListBinder<TItem, TView> : IListViewFactory<TItem, TView>
    where TView : Control
{
    private readonly Node _parent;
    private readonly PackedScene _scene;
    private readonly Action<TView, TItem> _bind;

    /// <param name="bind">Binds a row to its item; called once the row is in the tree, and again when the item is replaced.</param>
    public ListBinder(Node parent, PackedScene scene, Action<TView, TItem> bind)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(bind);
        _parent = parent;
        _scene = scene;
        _bind = bind;
    }

    public TView Create(TItem item, int index)
    {
        var view = _scene.Instantiate<TView>();
        _parent.AddChild(view);
        _parent.MoveChild(view, index);
        _bind(view, item);
        return view;
    }

    public void Update(TView view, TItem item) => _bind(view, item);

    public void Move(TView view, int index) => _parent.MoveChild(view, index);

    public void Destroy(TView view)
    {
        if (!GodotObject.IsInstanceValid(view)) return;
        // A queued free keeps the node in the parent until the frame ends, which would shift later indices.
        if (view.GetParent() == _parent) _parent.RemoveChild(view);
        view.QueueFree();
    }
}
