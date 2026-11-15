using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console;

/// <summary>
/// Brings an <see cref="ObservableList{T}"/> in line with a keyed source list using granular changes, so rows
/// keep their identity (and their bindings) across snapshots. Keys must be unique within the source.
/// </summary>
internal static class KeyedSync
{
    /// <param name="create">Builds the item for a new key (already up to date with the source).</param>
    /// <param name="update">Updates an existing item and returns what the slot should hold: the same instance
    /// for view models, a new record for immutable rows.</param>
    /// <remarks>Removed items that are <see cref="IDisposable"/> are disposed.</remarks>
    public static void Sync<TSource, TItem, TKey>(
        ObservableList<TItem> target,
        IReadOnlyList<TSource> source,
        Func<TSource, TKey> sourceKey,
        Func<TItem, TKey> itemKey,
        Func<TSource, TItem> create,
        Func<TItem, TSource, TItem> update)
        where TKey : notnull
    {
        var comparer = EqualityComparer<TKey>.Default;

        for (var i = target.Count - 1; i >= 0; i--)
        {
            var key = itemKey(target[i]);
            if (ContainsKey(source, key, sourceKey, comparer)) continue;
            var removed = target[i];
            target.RemoveAt(i);
            (removed as IDisposable)?.Dispose();
        }

        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var key = sourceKey(item);
            var found = -1;
            for (var j = i; j < target.Count; j++)
            {
                if (!comparer.Equals(itemKey(target[j]), key)) continue;
                found = j;
                break;
            }

            if (found < 0)
            {
                target.Insert(i, create(item));
                continue;
            }
            if (found != i) target.Move(found, i);
            target.Replace(i, update(target[i], item));
        }
    }

    /// <summary>Syncs immutable rows: equal rows raise nothing, changed rows are replaced in place.</summary>
    public static void SyncRows<TRow, TKey>(ObservableList<TRow> target, IReadOnlyList<TRow> rows, Func<TRow, TKey> key)
        where TKey : notnull =>
        Sync(target, rows, key, key, static row => row, static (_, row) => row);

    private static bool ContainsKey<TSource, TKey>(
        IReadOnlyList<TSource> source, TKey key, Func<TSource, TKey> sourceKey, EqualityComparer<TKey> comparer)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (comparer.Equals(sourceKey(source[i]), key)) return true;
        }
        return false;
    }
}
