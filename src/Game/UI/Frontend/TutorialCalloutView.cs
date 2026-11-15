using Godot;
using Usa.Game.Flow.Tutorial;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The guided-first-run callout (<c>TutorialCallout.tscn</c>, Figma <c>Tutorial callout</c> set 35-5331; UI.md 5.1): at most
/// 280 wide, <c>bg.2</c> with a 2 px <c>accent</c> left bar and a pointer arrow toward its anchor, title, one or two sentences,
/// for a typed-order step a send button with the exact order ("Send: ‘June, read the thruster temps’", submitted as a
/// structured request through the console, <see cref="ITutorialAnchors.SendOrder"/>), and "Continue" on an informational step
/// or "Got it" on an action step. Callouts never time out. Placed beside the anchor's on-screen rectangle by <see cref="CalloutPlacement"/> every frame (scaled console,
/// any window size, tab switches, scrolling), kept on screen and off the anchor, with a <see cref="TutorialHighlight"/> ring
/// around the anchor. While the anchor sits in a hidden tab or drawer, both point at the button that opens it. It never
/// blocks input; pressing a single-button anchor (<see cref="TutorialAnchorIds.CompletesOnPress"/>) counts as "Got it", and a
/// dropdown or the confirm dialog completes once its choice is made (<see cref="TutorialAnchorIds.CompletesOnChange"/>).
/// </summary>
public sealed partial class TutorialCalloutView : PanelContainer
{
    private const float ArrowPx = 8f;
    private const float ArrowInsetPx = 12f;

    // Hard-coded English like the rest of the demo console (DEMO_CUTS 8); "Got it" still comes from strings.json.
    private const string ContinueLabel = "Continue";

    private Label _header = null!;
    private Label _title = null!;
    private Label _text = null!;
    private Button _gotIt = null!;
    private Button _order = null!;
    private string _gotItLabel = "Got it";
    private string _headerLabel = string.Empty;
    private TutorialHighlight? _highlight;
    private TutorialCallouts? _callouts;
    private Func<string, Rect2?> _anchorRect = _ => null;
    private LayoutRect? _keepClearFor;
    private LayoutRect? _keepClear;
    private FrontendContext? _context;
    private CalloutSide _side = CalloutSide.Below;
    private bool _docked;
    private Rect2 _anchor;
    private TutorialAnchorHit? _hit;
    private string? _stateBeat;
    private string? _state;

    /// <summary>The beat on screen (diagnostics: the playthrough driver names its captures after it).</summary>
    public string? CurrentBeatId => _callouts is { Enabled: true } callouts ? callouts.Current?.BeatId : null;

    /// <summary>Where the ring is (canvas pixels) and whether it is on the target itself or on the tab/button that reveals it.</summary>
    public TutorialAnchorHit? CurrentHit => Visible ? _hit : null;

    public override void _Ready()
    {
        _header = GetNode<Label>("%Header");
        _title = GetNode<Label>("%CalloutTitle");
        _text = GetNode<Label>("%Text");
        _gotIt = GetNode<Button>("%GotIt");
        _order = GetNode<Button>("%Order");
        _gotIt.Pressed += () => _callouts?.Dismiss();
        _order.Pressed += () => _callouts?.RequestOrder();
        Visible = false;
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        Detach();
        if (_highlight is not null && IsInstanceValid(_highlight)) _highlight.QueueFree();
        _highlight = null;
    }

    public void Bind(TutorialCallouts callouts, Func<string, Rect2?> anchorRect, FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(callouts);
        ArgumentNullException.ThrowIfNull(anchorRect);
        ArgumentNullException.ThrowIfNull(context);
        Detach();
        _callouts = callouts;
        _anchorRect = anchorRect;
        _context = context;
        var tokens = context.Tokens;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = tokens.ColorBg2,
            BorderColor = tokens.ColorAccent,
            BorderWidthLeft = 2,
            CornerRadiusTopRight = tokens.RadiusPanelPx,
            CornerRadiusBottomRight = tokens.RadiusPanelPx,
            ContentMarginLeft = tokens.Space16Px,
            ContentMarginRight = tokens.Space16Px,
            ContentMarginTop = tokens.Space12Px,
            ContentMarginBottom = tokens.Space12Px,
        });
        _headerLabel = context.Text.Get("briefing.calloutHeader").ToUpperInvariant();
        _header.Text = _headerLabel;
        _gotItLabel = context.Text.Get("briefing.calloutGotIt");
        EnsureHighlight().Configure(tokens.ColorAccent, context.ReducedMotion);
        callouts.Changed += Render;
        callouts.OrderRequested += OnOrderRequested;
        Render();
    }

    private void Detach()
    {
        if (_callouts is null) return;
        _callouts.Changed -= Render;
        _callouts.OrderRequested -= OnOrderRequested;
    }

    // The console (group TutorialAnchorIds.Group) owns the run's command path and its confirmation dialog.
    private void OnOrderRequested(Usa.Simulation.Guidance.TutorialOrder order)
    {
        if (IsInsideTree() && GetTree().GetFirstNodeInGroup(TutorialAnchorIds.Group) is ITutorialAnchors anchors) anchors.SendOrder(order);
    }

    // Presentation only: follows the anchor while it moves (tabs switch, panels scroll or expand, the window resizes).
    public override void _Process(double delta)
    {
        Place();
        WatchState();
    }

    // A CompletesOnChange beat completes once its anchor state differs from when the beat showed (an item chosen, the order held).
    private void WatchState()
    {
        if (_callouts?.Current is not { } current || !TutorialAnchorIds.CompletesOnChange.Contains(current.Anchor)) return;
        if (!IsInsideTree() || GetTree().GetFirstNodeInGroup(TutorialAnchorIds.Group) is not ITutorialAnchors anchors) return;
        // A railed dropdown step completes on its own choice, even one already selected when the step showed.
        if (_callouts.StepOf(current) is { Phase: Usa.Simulation.Guidance.TutorialRailsPhase.Do, Rail.Choose: { } item } && anchors.IsChosen(current.Anchor, item))
        {
            _stateBeat = null;
            _callouts.Dismiss();
            return;
        }
        if (anchors.StateOf(current.Anchor) is not { } state) return;
        if (_stateBeat != current.BeatId)
        {
            _stateBeat = current.BeatId;
            _state = state;
            return;
        }
        if (state == _state) return;
        _stateBeat = null;
        _callouts.Dismiss();
    }

    // Never consumes the event: the press still reaches the console control under the ring.
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } press || !Visible) return;
        if (_callouts?.Current is not { } current || _hit is not { IsTarget: true }) return;
        if (!TutorialAnchorIds.CompletesOnPress.Contains(current.Anchor) || TutorialAnchorIds.CompletesOnChange.Contains(current.Anchor)) return;
        // The press's own position (viewport pixels, like the anchor rectangle), not the cursor read back afterwards.
        if (!_anchor.HasPoint(press.Position)) return;
        var beatId = current.BeatId;
        Callable.From(() =>
        {
            if (_callouts?.Current?.BeatId == beatId) _callouts.Dismiss();
        }).CallDeferred();
    }

    public override void _Draw()
    {
        if (_context is null) return;
        var bounds = new LayoutRect(GlobalPosition.X, GlobalPosition.Y, Size.X, Size.Y);
        var anchor = new LayoutRect(_anchor.Position.X, _anchor.Position.Y, _anchor.Size.X, _anchor.Size.Y);
        if (_docked) return;
        var (x, y) = CalloutPlacement.ArrowBase(bounds, _side, anchor, ArrowInsetPx);
        var b = new Vector2(x, y) - GlobalPosition;
        Vector2[] arrow = _side switch
        {
            CalloutSide.Below => [b + new Vector2(-ArrowPx, 0), b + new Vector2(ArrowPx, 0), b + new Vector2(0, -ArrowPx)],
            CalloutSide.Above => [b + new Vector2(-ArrowPx, 0), b + new Vector2(ArrowPx, 0), b + new Vector2(0, ArrowPx)],
            CalloutSide.Right => [b + new Vector2(0, -ArrowPx), b + new Vector2(0, ArrowPx), b + new Vector2(-ArrowPx, 0)],
            _ => [b + new Vector2(0, -ArrowPx), b + new Vector2(0, ArrowPx), b + new Vector2(ArrowPx, 0)],
        };
        DrawColoredPolygon(arrow, _context.Tokens.ColorBg2);
        DrawPolyline([arrow[0], arrow[2], arrow[1]], _context.Tokens.ColorAccent, 1f, true);
    }

    // A sibling under the callout in the callout layer, so the ring is above the console and below the callout.
    private TutorialHighlight EnsureHighlight()
    {
        if (_highlight is not null && IsInstanceValid(_highlight)) return _highlight;
        _highlight = new TutorialHighlight { Name = "TutorialHighlight" };
        if (GetParent() is { } host)
        {
            host.AddChild(_highlight);
            host.MoveChild(_highlight, GetIndex());
        }
        else
        {
            _highlight.TopLevel = true;
            AddChild(_highlight);
        }
        return _highlight;
    }

    private void Render()
    {
        var current = _callouts is { Enabled: true } callouts ? callouts.Current : null;
        Visible = current is not null;
        SetProcess(current is not null);
        if (current is null)
        {
            _hit = null;
            _highlight?.Show(null);
            return;
        }
        // On the rails: "Step 4 of 23", the exact action under the text, and no button that skips an action step.
        var step = _callouts!.StepOf(current);
        _header.Text = step is null ? _headerLabel : step.Header;
        _title.Text = current.Title ?? string.Empty;
        _title.Visible = !string.IsNullOrEmpty(current.Title);
        _text.Text = step is null || current.Text == step.Instruction ? current.Text : current.Text + "\n\n" + step.Instruction;
        _order.Visible = current.OrderLabel is not null;
        _order.Text = current.OrderLabel ?? string.Empty;
        _gotIt.Text = current.IsInformational ? ContinueLabel : _gotItLabel;
        _gotIt.Visible = step is null || (step.Rail.Skip && step.Phase == Usa.Simulation.Guidance.TutorialRailsPhase.Do);
        CustomMinimumSize = new Vector2(TutorialCallouts.MaxWidthPx, 0);
        Size = new Vector2(TutorialCallouts.MaxWidthPx, 0);
        Place();
    }

    private LayoutRect? AvoidRect() =>
        IsInsideTree() && GetTree().GetFirstNodeInGroup(TutorialAnchorIds.Group) is ITutorialAnchors anchors ? anchors.TryGetAvoidRect() : null;

    private void Place()
    {
        if (_callouts?.Current is not { } current || Resolve(current.Anchor) is not { } hit)
        {
            _hit = null;
            Visible = false;
            _highlight?.Show(null);
            return;
        }
        _hit = hit;
        Visible = true;
        var anchor = new Rect2(hit.Rect.X, hit.Rect.Y, hit.Rect.Width, hit.Rect.Height);
        var area = GetParent() is Control { Size: { X: > 0, Y: > 0 } } host ? host.GetGlobalRect() : GetViewportRect();
        var size = GetCombinedMinimumSize();
        if (_keepClearFor != hit.Rect)
        {
            _keepClearFor = hit.Rect;
            _keepClear = PanelAround(hit.Rect, area);
        }
        // The Proceed button sits on the recovery card inside the view: its callout may stand beside the card, nothing else over the pod.
        var avoid = current.Anchor == TutorialAnchorIds.ProceedDebrief ? null : AvoidRect();
        var placement = CalloutPlacement.Place(
            hit.Rect, _keepClear, TutorialCallouts.MaxWidthPx, size.Y,
            new LayoutRect(area.Position.X, area.Position.Y, area.Size.X, area.Size.Y), avoid);
        var bounds = placement.Bounds;
        var position = new Vector2(Mathf.Round(bounds.X), Mathf.Round(bounds.Y));
        var moved = _side != placement.Side || _docked != placement.Docked || _anchor != anchor || GlobalPosition != position;
        _side = placement.Side;
        _docked = placement.Docked;
        _anchor = anchor;
        GlobalPosition = position;
        Size = new Vector2(bounds.Width, size.Y);
        // A docked card is not about a spot in the view, so it carries no ring over the pod.
        _highlight?.Show(placement.Docked ? null : anchor);
        if (moved) QueueRedraw();
    }

    /// <summary>
    /// The panel the anchor sits in (smallest visible panel around it, up to about a third of the screen), which the
    /// callout keeps clear so it never hides what the target belongs to (the readout beside an Ask button, the planner
    /// around its controls). Null when the anchor is not inside such a panel.
    /// </summary>
    private LayoutRect? PanelAround(LayoutRect anchor, Rect2 area)
    {
        if (!IsInsideTree() || GetTree().GetFirstNodeInGroup(TutorialAnchorIds.Group) is not Node root) return null;
        var limit = area.Size.X * area.Size.Y * 0.35f;
        var anchorArea = anchor.Width * anchor.Height;
        LayoutRect? best = null;
        var bestArea = float.MaxValue;
        foreach (var panel in root.FindChildren("*", "", recursive: true, owned: false).OfType<PanelContainer>())
        {
            if (!panel.IsVisibleInTree()) continue;
            var r = panel.GetGlobalRect();
            var rect = new LayoutRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);
            var size = rect.Width * rect.Height;
            if (size <= anchorArea * 1.5f || size > limit || size >= bestArea || !rect.Contains(anchor)) continue;
            best = rect;
            bestArea = size;
        }
        return best;
    }

    // The console (group TutorialAnchorIds.Group) also says whether the rectangle is a tab or drawer button on the way.
    private TutorialAnchorHit? Resolve(string anchorId)
    {
        if (IsInsideTree() && GetTree().GetFirstNodeInGroup(TutorialAnchorIds.Group) is ITutorialAnchors anchors)
            return anchors.Resolve(anchorId);
        return _anchorRect(anchorId) is { } r
            ? new TutorialAnchorHit(new LayoutRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y), true)
            : null;
    }
}
