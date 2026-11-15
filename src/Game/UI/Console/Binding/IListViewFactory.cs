namespace Usa.Game.UI.Console.Binding;

/// <summary>Creates, updates, orders and frees the views a <see cref="ListMirror{TItem, TView}"/> keeps.</summary>
public interface IListViewFactory<in TItem, TView>
{
    /// <summary>Creates and binds a view, placed at <paramref name="index"/> among the mirror's views.</summary>
    TView Create(TItem item, int index);

    /// <summary>Rebinds an existing view to the item that replaced its old one.</summary>
    void Update(TView view, TItem item);

    /// <summary>Places an existing view at <paramref name="index"/>.</summary>
    void Move(TView view, int index);

    /// <summary>Unbinds and frees a view.</summary>
    void Destroy(TView view);
}
