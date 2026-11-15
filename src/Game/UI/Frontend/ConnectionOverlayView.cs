using Godot;
using Usa.Game.Flow.Screens;
using Usa.Game.Services.Connection;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The connection freeze overlay (<c>ConnectionOverlay.tscn</c>, Figma G6-11 = G2-10; UI.md 5, GDD 15.6): an opaque
/// <c>bg.0</c> cover over everything, the Esc menu included, with one status line per essential service, the one-line hint (what
/// to do), the retry countdown and "Retry now", which goes to <c>GameServices.Backend.Retry()</c> (probe now, restart a proxy the
/// game launched) or, without a tracker, straight to a probe. It never shows mission data. Visible while
/// <c>ScreenRouter.ConnectionOverlayVisible</c>; the flow resumes by itself when the monitor reports the link restored.
/// </summary>
public sealed partial class ConnectionOverlayView : Control
{
    private ColorRect _cover = null!;
    private Label _title = null!;
    private Label _body = null!;
    private VBoxContainer _lines = null!;
    private Label _countdown = null!;
    private Button _retry = null!;
    private Label _noDoubleExecution = null!;
    private Label _notAFailure = null!;
    private Label _hint = null!;
    private FrontendContext? _context;
    private ConnectionOverlayModel? _model;

    public override void _Ready()
    {
        _cover = GetNode<ColorRect>("%Cover");
        _title = GetNode<Label>("%Title");
        _body = GetNode<Label>("%Body");
        _lines = GetNode<VBoxContainer>("%Lines");
        _countdown = GetNode<Label>("%Countdown");
        _retry = GetNode<Button>("%Retry");
        _noDoubleExecution = GetNode<Label>("%NoDoubleExecution");
        _notAFailure = GetNode<Label>("%NotAFailure");
        _hint = GetNode<Label>("%Hint");
        _retry.Pressed += () => _model?.RetryNow();
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        if (_context is null || _model is null) return;
        _context.Services.Connection.ReportUpdated -= _model.Update;
        _model.Changed -= RenderLines;
        _model.RetryRequested -= OnRetryRequested;
    }

    private void OnRetryRequested()
    {
        if (_context is not { } context) return;
        if (context.Services.Backend is { } backend) backend.Retry();
        else context.Services.Connection.ProbeNow();
    }

    // Presentation only: the retry countdown while the cover shows.
    public override void _Process(double delta)
    {
        if (_model is not null && _context is not null)
            _countdown.Text = _model.Countdown(_context.Services.Connection.SecondsUntilNextProbe);
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _model = new ConnectionOverlayModel(context.Router, context.Strings);
        _cover.Color = context.Tokens.ColorBg0;
        _title.Text = _model.Title;
        _body.Text = _model.Body;
        _retry.Text = _model.RetryNowLabel + "  Enter";
        _noDoubleExecution.Text = _model.NoDoubleExecution;
        _notAFailure.Text = _model.NotAFailure;

        var connection = context.Services.Connection;
        connection.ReportUpdated += _model.Update;
        _model.RetryRequested += OnRetryRequested;
        _model.Changed += RenderLines;
        _model.Update(connection.LastReport);
        Refresh();
    }

    /// <summary>Follows the router (the shell calls it on every change).</summary>
    public void Refresh()
    {
        var visible = _model?.Visible ?? false;
        if (Visible == visible) return;
        Visible = visible;
        SetProcess(visible);
        if (visible) _retry.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void RenderLines()
    {
        if (_model is null || _context is null) return;
        _hint.Text = _model.Hint ?? string.Empty;
        _hint.Visible = _model.Hint is not null;
        Ui.Clear(_lines);
        foreach (var line in _model.Lines)
        {
            var row = Ui.Row(12);
            var label = Ui.Label(line.Label);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            var tone = line.State switch
            {
                OverlayServiceState.Ok => "Ok",
                OverlayServiceState.Reconnecting => "Caution",
                _ => "Critical",
            };
            row.AddChild(Ui.Badge(line.StateText, tone));
            _lines.AddChild(row);
        }
    }
}
