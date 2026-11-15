namespace Usa.Game.Presentation.ViewModels;

public enum ListChangeKind
{
    Inserted,
    Removed,
    Replaced,
    Moved,
    /// <summary>The whole content changed (clear or bulk replace); rebuild from the list.</summary>
    Reset,
}

/// <summary>One granular change to an <see cref="ObservableList{T}"/>, raised after it is applied.</summary>
/// <param name="Index">Inserted: new index. Removed: old index. Replaced: index. Moved: new index. Reset: -1.</param>
/// <param name="OldIndex">Moved: index before the move; otherwise equal to <paramref name="Index"/>.</param>
/// <param name="Item">Inserted/Replaced: new item. Removed/Moved: the item. Reset: default.</param>
/// <param name="OldItem">Replaced: the item that was replaced; otherwise default.</param>
public readonly record struct ListChange<T>(ListChangeKind Kind, int Index, int OldIndex, T Item, T OldItem);

/// <summary>Factories for <see cref="ListChange{T}"/>.</summary>
public static class ListChange
{
    public static ListChange<T> Inserted<T>(int index, T item) => new(ListChangeKind.Inserted, index, index, item, default!);

    public static ListChange<T> Removed<T>(int index, T item) => new(ListChangeKind.Removed, index, index, item, default!);

    public static ListChange<T> Replaced<T>(int index, T oldItem, T newItem) => new(ListChangeKind.Replaced, index, index, newItem, oldItem);

    public static ListChange<T> Moved<T>(int oldIndex, int newIndex, T item) => new(ListChangeKind.Moved, newIndex, oldIndex, item, default!);

    public static ListChange<T> Reset<T>() => new(ListChangeKind.Reset, -1, -1, default!, default!);
}
