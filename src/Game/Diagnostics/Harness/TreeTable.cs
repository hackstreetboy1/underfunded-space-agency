using Godot;

namespace Usa.Game.Diagnostics.Harness;

/// <summary>
/// Keyed rows on a <see cref="Tree"/> that update in place, so refreshing keeps the scroll position. Call
/// <see cref="Begin"/>, then <see cref="Row"/> for every row to show, then <see cref="End"/> to drop rows not shown.
/// </summary>
public sealed class TreeTable
{
    private readonly Tree _tree;
    private readonly TreeItem _root;
    private readonly Dictionary<string, TreeItem> _rows = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public TreeTable(Tree tree, params string[] titles)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(titles);
        _tree = tree;
        _tree.Clear();
        _tree.HideRoot = true;
        _root = _tree.CreateItem();
        SetColumns(titles);
    }

    /// <summary>Replaces the columns (and clears the rows) when the titles differ from the current ones.</summary>
    public void SetColumns(params string[] titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        if (_tree.Columns == titles.Length && Enumerable.Range(0, titles.Length).All(i => _tree.GetColumnTitle(i) == titles[i])) return;
        Clear();
        _tree.Columns = titles.Length;
        _tree.ColumnTitlesVisible = true;
        for (var i = 0; i < titles.Length; i++)
        {
            _tree.SetColumnTitle(i, titles[i]);
            _tree.SetColumnExpand(i, true);
            _tree.SetColumnExpandRatio(i, i == 0 ? 2 : 1);
            _tree.SetColumnClipContent(i, true);
        }
    }

    public void Begin() => _seen.Clear();

    public void Row(string key, params string[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _seen.Add(key);
        if (!_rows.TryGetValue(key, out var item))
        {
            item = _tree.CreateItem(_root);
            _rows[key] = item;
        }
        for (var i = 0; i < _tree.Columns; i++)
        {
            var text = i < cells.Length ? cells[i] : "";
            if (item.GetText(i) != text) item.SetText(i, text);
        }
    }

    public void End()
    {
        if (_seen.Count == _rows.Count) return;
        foreach (var key in _rows.Keys.Where(k => !_seen.Contains(k)).ToArray())
        {
            _rows[key].Free();
            _rows.Remove(key);
        }
    }

    public void Clear()
    {
        foreach (var item in _rows.Values) item.Free();
        _rows.Clear();
        _seen.Clear();
    }
}
