namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// Which rows of a long, fixed-row-height list are on screen (the event log keeps up to 5000 rows but draws only a screenful,
/// so it needs no node per row). The scroll offset is in pixels from the top; while <see cref="FollowNewest"/> holds, the
/// window stays on the newest rows as rows are added, and scrolling up releases it until the player returns to the bottom.
/// </summary>
public sealed class VirtualListWindow
{
    private int _count;
    private double _viewportPx;
    private double _scrollPx;

    public VirtualListWindow(int rowHeightPx)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rowHeightPx, 1);
        RowHeightPx = rowHeightPx;
    }

    public int RowHeightPx { get; }

    public int Count => _count;

    /// <summary>Scroll offset from the top, 0 to <see cref="MaxScrollPx"/>.</summary>
    public double ScrollPx => _scrollPx;

    public double ViewportPx => _viewportPx;

    /// <summary>Total height of every row.</summary>
    public double ContentPx => (double)_count * RowHeightPx;

    public double MaxScrollPx => Math.Max(0, ContentPx - _viewportPx);

    /// <summary>Stick to the newest (bottom) rows as rows arrive.</summary>
    public bool FollowNewest { get; private set; } = true;

    /// <summary>First row index on screen (possibly partly hidden at the top).</summary>
    public int FirstIndex => _count == 0 ? 0 : Math.Min(_count - 1, (int)(_scrollPx / RowHeightPx));

    /// <summary>Pixels the first visible row is shifted up by.</summary>
    public double FirstRowOffsetPx => _scrollPx - (double)FirstIndex * RowHeightPx;

    /// <summary>Rows to draw from <see cref="FirstIndex"/>, including a partly visible one at each end.</summary>
    public int VisibleCount => _count == 0 ? 0 : Math.Min(_count - FirstIndex, PoolSize);

    /// <summary>How many row views a viewport this tall needs at most.</summary>
    public int PoolSize => (int)Math.Ceiling(_viewportPx / RowHeightPx) + 1;

    public void SetViewport(double heightPx)
    {
        _viewportPx = double.IsFinite(heightPx) ? Math.Max(0, heightPx) : 0;
        Clamp();
    }

    /// <summary>The list now holds <paramref name="count"/> rows.</summary>
    public void SetCount(int count)
    {
        _count = Math.Max(0, count);
        Clamp();
    }

    /// <summary>The player scrolled to <paramref name="scrollPx"/>; reaching the bottom follows the newest rows again.</summary>
    public void ScrollTo(double scrollPx)
    {
        _scrollPx = double.IsFinite(scrollPx) ? Math.Clamp(scrollPx, 0, MaxScrollPx) : 0;
        FollowNewest = _scrollPx >= MaxScrollPx - 1;
    }

    /// <summary>Mouse wheel or arrow keys, in rows (positive scrolls towards newer rows).</summary>
    public void ScrollRows(double rows) => ScrollTo(_scrollPx + rows * RowHeightPx);

    private void Clamp()
    {
        _scrollPx = FollowNewest ? MaxScrollPx : Math.Clamp(_scrollPx, 0, MaxScrollPx);
    }
}
