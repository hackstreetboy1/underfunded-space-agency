using Godot;
using Usa.Game.Presentation.Console.EventLog;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// The event log's rows (<c>VirtualLogList.tscn</c>; UI.md 3.8): a virtualized list that keeps a screenful of
/// <c>EventLogRow.tscn</c> views and rebinds them as the window moves, so thousands of rows cost no nodes. A list change only
/// marks the window dirty; the views are rebound once per frame at most. Stays on the newest rows until the player scrolls up.
/// </summary>
public sealed partial class VirtualLogList : Control
{
    private const int RowHeightPx = 28;

    private readonly VirtualListWindow _window = new(RowHeightPx);
    private readonly List<EventLogRow> _pool = [];
    private VScrollBar _bar = null!;
    private Control _rows = null!;
    private ObservableList<LogRow>? _source;
    private IDisposable? _subscription;
    private ConsolePalette _palette = ConsolePalette.Default;
    private bool _dirty;
    private bool _settingBar;

    [Export]
    public PackedScene? RowScene { get; set; }

    public override void _Ready()
    {
        _bar = GetNode<VScrollBar>("%Bar");
        _rows = GetNode<Control>("%Rows");
        _bar.ValueChanged += value =>
        {
            if (_settingBar) return;
            _window.ScrollTo(value);
            MarkDirty();
        };
        Resized += MarkDirty;
        SetProcess(false);
    }

    public override void _ExitTree() => Unbind();

    // Presentation only: rebinds the visible rows once after any number of changes this frame.
    public override void _Process(double delta)
    {
        SetProcess(false);
        if (_dirty) Refresh();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        var rows = button.ButtonIndex switch
        {
            MouseButton.WheelUp => -3,
            MouseButton.WheelDown => 3,
            _ => 0,
        };
        if (rows == 0) return;
        _window.ScrollRows(rows);
        MarkDirty();
        AcceptEvent();
    }

    public void Bind(ObservableList<LogRow> rows, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _source = rows;
        _palette = palette;
        _subscription = rows.Subscribe(_ => MarkDirty());
        Refresh();
    }

    public void Unbind()
    {
        _subscription?.Dispose();
        _subscription = null;
        _source = null;
        _dirty = false;
        foreach (var row in _pool) row.Visible = false;
    }

    private void MarkDirty()
    {
        _dirty = true;
        SetProcess(true);
    }

    private void Refresh()
    {
        _dirty = false;
        var count = _source?.Count ?? 0;
        _window.SetViewport(_rows.Size.Y);
        _window.SetCount(count);
        EnsurePool(_window.PoolSize);

        _settingBar = true;
        _bar.MaxValue = _window.ContentPx;
        _bar.Page = _window.ViewportPx;
        _bar.Value = _window.ScrollPx;
        _bar.Visible = _window.MaxScrollPx > 0;
        _settingBar = false;

        var first = _window.FirstIndex;
        var visible = _window.VisibleCount;
        var offset = (float)_window.FirstRowOffsetPx;
        for (var i = 0; i < _pool.Count; i++)
        {
            var view = _pool[i];
            if (i >= visible || _source is null)
            {
                view.Visible = false;
                continue;
            }
            view.Visible = true;
            view.Position = new Vector2(0, i * RowHeightPx - offset);
            view.Size = new Vector2(_rows.Size.X, RowHeightPx);
            view.Bind(_source[first + i], _palette);
        }
    }

    private void EnsurePool(int size)
    {
        if (RowScene is null) return;
        while (_pool.Count < size)
        {
            var view = RowScene.Instantiate<EventLogRow>();
            _rows.AddChild(view);
            view.Visible = false;
            _pool.Add(view);
        }
    }
}
