using Godot;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.Diagram;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Console.Views;

/// <summary>
/// Draws one <see cref="DiagramFrame"/> (custom <c>_Draw</c>; UI.md 4.5, Gate 3 decisions): compartments as rounded
/// <c>bg.2</c> rectangles with labels, connections with their own colour and line pattern (power solid, fluid dash-dot,
/// propellant dashed, data dotted), nodes in the status language (filled circle, octagon with a thick border, dashed triangle,
/// dashed circle), the selected node's supply path lit. The JSON layout is drawn ×1.5 at 100 % zoom; it opens at Fit. Wheel
/// zooms, a right or middle drag pans; below 100 % node labels declutter. Hover and click are reported to the owner.
/// </summary>
public sealed partial class DiagramCanvas : Control
{
    /// <summary>JSON layout pixels to screen pixels at 100 % zoom (Gate 3: the only legible scale).</summary>
    public const float BaseScale = 1.5f;

    private const float MinZoom = 0.3f;
    private const float MaxZoom = 3f;
    private const float FitMarginPx = 24f;

    private DiagramFrame? _frame;
    private ConsolePalette _palette = ConsolePalette.Default;
    private float _zoom = 1f;
    private Vector2 _pan;
    private bool _fitPending = true;
    private bool _panning;
    private double _nodeRadius = DiagramLayoutOptions.Default.NodeRadiusPx;

    /// <summary>The pointer moved onto a node (or off every node: null).</summary>
    public event Action<ComponentId?, Vector2>? HoverChanged;

    /// <summary>A click on a node (or on empty space: null).</summary>
    public event Action<ComponentId?>? Clicked;

    /// <summary>The scene the frames come from, for hit testing.</summary>
    public DiagramScene? Scene { get; set; }

    public float Zoom => _zoom;

    public void Show(DiagramFrame frame, ConsolePalette palette, double nodeRadiusPx)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(palette);
        _frame = frame;
        _palette = palette;
        _nodeRadius = nodeRadiusPx;
        if (_fitPending) Fit();
        QueueRedraw();
    }

    /// <summary>Frames every visible compartment.</summary>
    public void Fit()
    {
        if (_frame is not { Compartments.Count: > 0 } frame || Size.X <= 0 || Size.Y <= 0)
        {
            _fitPending = true;
            return;
        }
        _fitPending = false;
        var bounds = Bounds(frame);
        var zoom = Math.Min((Size.X - 2 * FitMarginPx) / (float)(bounds.Width * BaseScale), (Size.Y - 2 * FitMarginPx) / (float)(bounds.Height * BaseScale));
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var scale = BaseScale * _zoom;
        _pan = Size / 2 - new Vector2((float)bounds.Centre.X, (float)bounds.Centre.Y) * scale;
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && _fitPending) Fit();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } wheel:
                ZoomAt(wheel.Position, 1.1f);
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } wheel:
                ZoomAt(wheel.Position, 1 / 1.1f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag:
                _panning = drag.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                Clicked?.Invoke(HitAt(click.Position));
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _panning:
                _pan += motion.Relative;
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                HoverChanged?.Invoke(HitAt(motion.Position), motion.Position);
                break;
        }
    }

    public override void _Draw()
    {
        if (_frame is not { } frame) return;
        var scale = BaseScale * _zoom;
        var font = GetThemeDefaultFont();
        var fontSize = GetThemeDefaultFontSize();

        foreach (var compartment in frame.Compartments)
        {
            var rect = ToScreen(compartment.Bounds, scale);
            var box = new StyleBoxFlat
            {
                BgColor = _palette.Of(ColourTokens.Bg2),
                BorderColor = _palette.Tokens.ColorLine1,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
            };
            box.SetBorderWidthAll(1);
            DrawStyleBox(box, rect);
            DrawString(font, rect.Position + new Vector2(8, fontSize + 4), compartment.Label, HorizontalAlignment.Left, rect.Size.X - 16, fontSize,
                _palette.Of(ColourTokens.TextLo));
        }

        foreach (var view in frame.Connections) DrawConnection(view, scale);

        var radius = (float)(_nodeRadius * _zoom);
        foreach (var view in frame.Nodes)
        {
            var at = ToScreen(view.Node.Position, scale);
            DrawNode(view, at, radius);
            if (_zoom >= 1f || view.Selected || view.Hovered)
                DrawString(font, at + new Vector2(-60, radius + fontSize + 2), view.Node.Name, HorizontalAlignment.Center, 120, fontSize - 2,
                    _palette.Of(view.Display.Dimmed ? ColourTokens.TextMid : ColourTokens.TextHi));
            if (view.Display.Badge is { } badge)
                DrawString(font, at + new Vector2(radius + 4, -radius), badge, HorizontalAlignment.Left, -1, fontSize - 3, _palette.Of(ColourTokens.TextMid));
        }
    }

    private void DrawConnection(DiagramConnectionView view, float scale)
    {
        var style = view.Connection.Style;
        var colour = (style.Color.StartsWith('#') ? Color.FromHtml(style.Color) : _palette.Of(style.Color)) with { A = (float)style.Opacity };
        var width = (float)style.WidthPx + (view.Highlighted ? 2 : 0);
        if (view.Highlighted) colour = _palette.Of(ColourTokens.Accent);
        var points = view.Connection.Points;
        for (var i = 1; i < points.Count; i++)
        {
            var from = ToScreen(points[i - 1], scale);
            var to = ToScreen(points[i], scale);
            switch (style.Pattern)
            {
                case LinePattern.Solid:
                    DrawLine(from, to, colour, width, antialiased: true);
                    break;
                case LinePattern.Dotted:
                    DrawDashedLine(from, to, colour, width, 2f);
                    break;
                default:
                    DrawDashedLine(from, to, colour, width, style.Pattern == LinePattern.Dashed ? 8f : 5f);
                    break;
            }
        }
    }

    private void DrawNode(DiagramNodeView view, Vector2 at, float radius)
    {
        var display = view.Display;
        var colour = _palette.Of(display.Dimmed ? ColourTokens.TextMid : display.ColorToken);
        var outline = display.ThickBorder ? 3f : 1.5f;
        switch (display.ShapeKey)
        {
            case "status.failed":
                var octagon = Polygon(at, radius, 8, Mathf.Pi / 8);
                DrawColoredPolygon(octagon, colour);
                DrawPolyline(Closed(octagon), _palette.Of(ColourTokens.TextHi), outline, antialiased: true);
                break;
            case "status.suspect":
                DrawDashedOutline(Polygon(at, radius, 3, -Mathf.Pi / 2), colour, outline);
                break;
            case "status.unknown":
                DrawDashedOutline(Polygon(at, radius, 16, 0), colour, outline);
                break;
            default:
                DrawCircle(at, radius, colour);
                break;
        }
        if (view.Highlighted) DrawArc(at, radius + 4, 0, Mathf.Tau, 24, _palette.Of(ColourTokens.Info), 2, antialiased: true);
        if (view.Selected) DrawArc(at, radius + 7, 0, Mathf.Tau, 24, _palette.Of(ColourTokens.Accent), 2, antialiased: true);
        else if (view.Hovered) DrawArc(at, radius + 6, 0, Mathf.Tau, 24, _palette.Of(ColourTokens.TextHi), 1, antialiased: true);
    }

    private void DrawDashedOutline(Vector2[] polygon, Color colour, float width)
    {
        for (var i = 0; i < polygon.Length; i++) DrawDashedLine(polygon[i], polygon[(i + 1) % polygon.Length], colour, width, 3f);
    }

    private void ZoomAt(Vector2 pointer, float factor)
    {
        var zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        var applied = zoom / _zoom;
        _pan = pointer - (pointer - _pan) * applied;
        _zoom = zoom;
        _fitPending = false;
        QueueRedraw();
    }

    private ComponentId? HitAt(Vector2 screen)
    {
        if (Scene is not { } scene) return null;
        var scale = BaseScale * _zoom;
        var point = (screen - _pan) / scale;
        return scene.HitTest(new DiagramPoint(point.X, point.Y)).Component;
    }

    private Vector2 ToScreen(DiagramPoint point, float scale) => new Vector2((float)point.X, (float)point.Y) * scale + _pan;

    private Rect2 ToScreen(DiagramRect rect, float scale) =>
        new(ToScreen(new DiagramPoint(rect.X, rect.Y), scale), new Vector2((float)rect.Width, (float)rect.Height) * scale);

    private static DiagramRect Bounds(DiagramFrame frame)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var compartment in frame.Compartments)
        {
            minX = Math.Min(minX, compartment.Bounds.X);
            minY = Math.Min(minY, compartment.Bounds.Y);
            maxX = Math.Max(maxX, compartment.Bounds.Right);
            maxY = Math.Max(maxY, compartment.Bounds.Bottom);
        }
        return new DiagramRect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private static Vector2[] Polygon(Vector2 centre, float radius, int sides, float startAngle)
    {
        var points = new Vector2[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = startAngle + Mathf.Tau * i / sides;
            points[i] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }

    private static Vector2[] Closed(Vector2[] polygon)
    {
        var closed = new Vector2[polygon.Length + 1];
        polygon.CopyTo(closed, 0);
        closed[^1] = polygon[0];
        return closed;
    }
}
