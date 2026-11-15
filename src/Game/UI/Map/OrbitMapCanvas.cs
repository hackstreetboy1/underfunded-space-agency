using Godot;
using Usa.Game.Input;
using Usa.Game.Presentation.Map;
using Usa.Game.UI.Console;
using Usa.Game.UI.Map.Geometry;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Map;

/// <summary>
/// The orbit map canvas (700 × 560 docked, the whole view in the compact layout; Figma G4-01 node 34:2129): draws an <see cref="OrbitMapFrame"/> with the approved
/// ×11 altitude scale (<see cref="AltitudeExaggeration"/>) and forwards wheel zoom, drag pan, node drag, handle picks and
/// free-angle drags. Drawing is split over child layers so the translucent ones (land, uncertainty band, corridor,
/// free-angle ring) take their opacity from <c>modulate.a</c> over full-strength token colours (Gate 4 decision 7).
/// </summary>
public sealed partial class OrbitMapCanvas : Control
{
    private const float PathWidth = 2;
    private const float ZoneWidth = 4;
    private const float ZoneGapPx = (float)OrbitMapDrawList.ZoneGapPx;
    private const float LabelGapPx = 18;
    private const float HandlePickPx = 9;
    private const float NodePickPx = 12;
    private const float RingPickPx = 6;
    private const double ZoomStep = 1.25;
    private const float MarkerCullPx = 400;

    private Control _landLayer = null!;
    private Control _baseLayer = null!;
    private Control _bandLayer = null!;
    private Control _corridorLayer = null!;
    private Control _pathLayer = null!;
    private Control _ringLayer = null!;

    // Built once per rebuild, emitted by the layer draws: clipped to the canvas, so zooming in stays cheap.
    private readonly OrbitMapDrawList _drawList = new();
    private Vector2[] _buffer = new Vector2[256];
    private int[] _sequence = [];
    private int[] _fan = [];

    private OrbitMapModel? _model;
    private OrbitMapInput? _input;
    private OrbitMapFrame? _frame;
    private AltitudeExaggeration? _projection;
    private MapTrackingOverlay _tracking = MapTrackingOverlay.None;
    private ConsolePalette _palette = ConsolePalette.Default;
    private MapText? _text;
    private DragKind _drag;
    private MissionTime? _draggedNodeTime;

    /// <summary>The player dragged the maneuver node to a new time (forward to the planner's start time).</summary>
    public event Action<MissionTime>? NodeTimeChanged;

    /// <summary>The player picked a direction handle.</summary>
    public event Action<BurnDirection>? DirectionPicked;

    /// <summary>The player dragged the free-angle knob (degrees from prograde towards radial-out).</summary>
    public event Action<double>? CustomAngleChanged;

    /// <summary>The frame was rebuilt (zoom, pan, new input); overlay chips refresh from <see cref="Frame"/>.</summary>
    public event Action? FrameChanged;

    private enum DragKind
    {
        None,
        Pan,
        Node,
        Ring,
    }

    public OrbitMapFrame? Frame => _frame;

    public override void _Ready()
    {
        _landLayer = GetNode<Control>("%LandLayer");
        _baseLayer = GetNode<Control>("%BaseLayer");
        _bandLayer = GetNode<Control>("%BandLayer");
        _corridorLayer = GetNode<Control>("%CorridorLayer");
        _pathLayer = GetNode<Control>("%PathLayer");
        _ringLayer = GetNode<Control>("%RingLayer");
        _baseLayer.Draw += DrawBase;
        _landLayer.Draw += DrawLand;
        _bandLayer.Draw += DrawBands;
        _corridorLayer.Draw += DrawCorridorFill;
        _pathLayer.Draw += DrawPaths;
        _ringLayer.Draw += DrawRing;
        Resized += OnResized;
    }

    /// <param name="text">Map words: the landing verdict (<c>map.zoneInZone</c>, <c>map.zoneNearEdge</c>).</param>
    public void Bind(OrbitMapModel model, MapText text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        _model = model;
        _text = text;
        OnResized();
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        RedrawLayers();
    }

    public void Show(OrbitMapInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
        if (_drag != DragKind.Node) _draggedNodeTime = null;
        Rebuild();
    }

    public void ShowTracking(MapTrackingOverlay tracking)
    {
        ArgumentNullException.ThrowIfNull(tracking);
        _tracking = tracking;
        RedrawLayers();
    }

    public void ZoomBy(double factor)
    {
        if (_model is null) return;
        var centre = new MapPoint(Size.X / 2, Size.Y / 2);
        if (_input is { } input) CursorZoom.Apply(_model, factor, centre, input.Now);
        else _model.Zoom(factor, centre);
        Rebuild();
    }

    public void SetLabelsVisible(bool visible)
    {
        if (_model is null) return;
        _model.LabelsVisible = visible;
        Rebuild();
    }

    /// <summary>"Focus vehicle" (F): pans so the crewed vehicle's drawn marker sits at the canvas centre, zoom kept.</summary>
    public void FocusVehicle()
    {
        if (_model is null || _frame is not { Vehicles.Count: > 0 } frame || _projection is null) return;
        var drawn = _projection.Project(frame.Vehicles[0].Position);
        _model.Pan((Size.X / 2) - drawn.X, (Size.Y / 2) - drawn.Y);
        Rebuild();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_model is null || _projection is null) return;
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } wheel:
                Zoom(ZoomStep, wheel.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } wheel:
                Zoom(1 / ZoomStep, wheel.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed) BeginDrag(button.Position);
                else _drag = DragKind.None;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _drag != DragKind.None:
                ContinueDrag(motion);
                AcceptEvent();
                break;
            case InputEventKey key when InputMap.HasAction(InputBindings.FocusVehicle)
                && key.IsActionPressed(InputBindings.FocusVehicle, allowEcho: false, exactMatch: true):
                // Through the InputMap with exact modifiers: Ctrl+F stays Search, and a rebound key works (R3-12).
                FocusVehicle();
                AcceptEvent();
                break;
        }
    }

    private void Zoom(double factor, Vector2 at)
    {
        // Zoom about the drawn point under the cursor (the projection's scale changes with zoom).
        if (_input is { } input) CursorZoom.Apply(_model!, factor, ToMap(at), input.Now);
        else _model!.Zoom(factor, _projection!.Unproject(ToMap(at)));
        Rebuild();
        AcceptEvent();
    }

    private void BeginDrag(Vector2 position)
    {
        GrabFocus();
        _drag = DragKind.Pan;
        if (_frame?.Node is not { } node) return;
        var drawnNode = _projection!.Project(node.Position);
        var cursor = ToMap(position);
        for (var i = 0; i < 4; i++)
        {
            var tip = drawnNode + (node.Handles[i].Tip - node.Position);
            if (cursor.DistanceTo(tip) > HandlePickPx) continue;
            DirectionPicked?.Invoke(node.Handles[i].Direction);
            _drag = DragKind.None;
            return;
        }
        var fromNode = cursor.DistanceTo(drawnNode);
        if (fromNode <= NodePickPx) _drag = DragKind.Node;
        else if (Math.Abs(fromNode - node.RingRadiusPx) <= RingPickPx) _drag = DragKind.Ring;
    }

    private void ContinueDrag(InputEventMouseMotion motion)
    {
        var model = _model!;
        switch (_drag)
        {
            case DragKind.Pan:
                model.Pan(motion.Relative.X, motion.Relative.Y);
                Rebuild();
                break;
            case DragKind.Node when _input is { CurrentConic: { } conic } input:
                var (_, time) = model.ProjectPointer(conic, _projection!.Unproject(ToMap(motion.Position)), input.Now);
                if (time is not { } nodeTime) break;
                _draggedNodeTime = nodeTime;
                Rebuild();
                NodeTimeChanged?.Invoke(nodeTime);
                break;
            case DragKind.Ring when _frame?.Node is { } node:
                var drawnNode = _projection!.Project(node.Position);
                CustomAngleChanged?.Invoke(node.CustomAngleTowards(node.Position + (ToMap(motion.Position) - drawnNode)));
                break;
        }
    }

    private void OnResized()
    {
        if (_model is null || Size.X <= 0 || Size.Y <= 0) return;
        _model.SetViewport(Size.X, Size.Y);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_model is null || _input is null) return;
        var input = _draggedNodeTime is { } dragged ? _input with { NodeTime = dragged } : _input;
        _frame = _model.BuildFrame(input);
        _projection = AltitudeExaggeration.For(_frame.Earth);
        _drawList.Build(_frame, _projection, input, Size.X, Size.Y);
        RedrawLayers();
        FrameChanged?.Invoke();
    }

    private void RedrawLayers()
    {
        if (!IsNodeReady()) return;
        _landLayer.QueueRedraw();
        _baseLayer.QueueRedraw();
        _bandLayer.QueueRedraw();
        _corridorLayer.QueueRedraw();
        _pathLayer.QueueRedraw();
        _ringLayer.QueueRedraw();
    }

    private Color Token(string key) => _palette.Of(key);

    private void DrawBase()
    {
        if (_frame is null) return;
        var list = _drawList;
        var centre = V(list.Centre);
        if (list.DiscsAsPolygons)
        {
            FanRuns(_baseLayer, list.Halo, Token("map.atmosphere"));
            FanRuns(_baseLayer, list.Body, Token("bg.2"));
            FanRuns(_baseLayer, list.Body, Token("tint.info"));
        }
        else
        {
            _baseLayer.DrawCircle(centre, (float)list.HaloRadiusPx, Token("map.atmosphere"));
            _baseLayer.DrawCircle(centre, (float)list.BodyRadiusPx, Token("bg.2"));
            _baseLayer.DrawCircle(centre, (float)list.BodyRadiusPx, Token("tint.info"));
        }
        var interfaceRadius = (float)list.InterfaceRadiusPx;
        var sweep = (float)list.InterfaceDashSweepRad;
        var colour = Token("map.atmosphere");
        foreach (var angle in list.InterfaceDashAngles)
            _baseLayer.DrawArc(centre, interfaceRadius, (float)angle, (float)angle + sweep, 3, colour, 1, true);
    }

    private void DrawLand()
    {
        if (_frame is null) return;
        // Built clipped to the viewport per rebuild, so zoomed in no huge polygon reaches Godot's triangulator.
        Triangles(_landLayer, _drawList.LandTriangles, Token("text.off"));
    }

    private void DrawBands()
    {
        if (_frame is null) return;
        var triangles = _drawList.BandTriangles;
        if (triangles.Count < 3) return;
        // One triangle array for the whole band instead of a draw command per triangle.
        var points = Points(triangles, 0, triangles.Count);
        AddTriangles(_bandLayer, Sequence(triangles.Count), points, Token("info"));
    }

    private void DrawCorridorFill()
    {
        if (_frame is null) return;
        Triangles(_corridorLayer, _drawList.CorridorTriangles, Token("ok"));
    }

    private void Triangles(CanvasItem layer, IReadOnlyList<MapPoint> triangles, Color colour)
    {
        var count = triangles.Count - (triangles.Count % 3);
        if (count < 3) return;
        AddTriangles(layer, Sequence(count), Points(triangles, 0, count), colour);
    }

    private static void AddTriangles(CanvasItem layer, ReadOnlySpan<int> indices, ReadOnlySpan<Vector2> points, Color colour)
    {
        ReadOnlySpan<Color> colours = [colour];
        RenderingServer.CanvasItemAddTriangleArray(
            layer.GetCanvasItem(), indices, points, colours, ReadOnlySpan<Vector2>.Empty, ReadOnlySpan<int>.Empty,
            ReadOnlySpan<float>.Empty, default, -1);
    }

    private void DrawRing()
    {
        if (_frame?.Node is not { } node || _projection is not { } projection) return;
        var centre = V(projection.Project(node.Position));
        DrawDashedCircle(_ringLayer, centre, (float)node.RingRadiusPx, Token("text.mid"), 1, 3, 3);
        var custom = node.Handles[4];
        var knob = centre + (V(custom.UnitScreen) * (float)node.RingRadiusPx);
        _ringLayer.DrawCircle(knob, 4, Token("accent"));
    }

    private void DrawPaths()
    {
        if (_frame is not { } frame || _projection is not { } projection) return;
        var font = GetThemeFont("font", "ReadoutSmall");
        var fontSize = GetThemeFontSize("font_size", "ReadoutSmall");
        var centre = V(frame.Earth.Centre);
        var rim = (float)projection.DrawnBodyRadiusPx + ZoneGapPx;

        var metaFont = GetThemeFont("font", "Meta");
        var metaSize = GetThemeFontSize("font_size", "Meta");
        foreach (var station in frame.Stations ?? [])
        {
            var direction = (V(station.Surface) - centre).Normalized();
            _pathLayer.DrawLine(centre + (direction * (rim - 2)), centre + (direction * (rim + 6)), Token("text.lo"), 2);
            if (!frame.LabelsVisible) continue;
            var name = station.Label;
            var width = metaFont.GetStringSize(name, HorizontalAlignment.Left, -1, metaSize).X;
            var at = centre + (direction * (rim + 10));
            var origin = new Vector2(direction.X < -0.3f ? at.X - width : direction.X > 0.3f ? at.X : at.X - (width / 2), at.Y + (direction.Y > 0.3f ? metaSize : direction.Y < -0.3f ? 0 : metaSize / 2f));
            _pathLayer.DrawString(metaFont, origin, name, HorizontalAlignment.Left, -1, metaSize, Token("text.lo"));
        }

        var list = _drawList;
        for (var run = 0; run < list.ZoneArcs.Count; run++)
            _pathLayer.DrawPolyline(Run(list.ZoneArcs, run), Token(ZoneToken(list.ZoneArcRanks[run])), ZoneWidth, false);
        DrawDashes(list.ZoneAtLandingDashes, Token("info-ghost"), 2);

        Polylines(list.CurrentPath, Token("info"), PathWidth);
        Polylines(list.CorridorEdges, Token("caution"), 1);
        if (frame.Ghost is { } ghost)
        {
            DrawDashes(list.CoastDashes, Token("info-ghost"), PathWidth);
            DrawDashes(list.DescentDashes, Token("info-ghost"), PathWidth);
            if (ghost.Landing is { } landing) DrawLanding(landing, projection);
        }

        foreach (var apsis in frame.Apsides) DrawApsis(apsis, projection, centre, font, fontSize);
        DrawVehicles(frame, projection);
        if (frame.Node is { } node) DrawNode(node, projection);

        if (!frame.LabelsVisible) return;
        var labelFont = GetThemeFont("font", "Small");
        var labelSize = GetThemeFontSize("font_size", "Small");
        foreach (var zone in frame.Zones)
        {
            var mid = (zone.StartScreenAngleRad + zone.EndScreenAngleRad) / 2;
            var anchor = V(projection.OnCircle(rim + LabelGapPx, mid));
            DrawChip(anchor, Outward(mid), [zone.Label, zone.DetailText], labelFont, labelSize, Token(ZoneToken(zone.Rank)));
        }
        foreach (var corridor in Corridors())
        {
            var at = V(projection.Project(corridor.InterfacePoint));
            DrawChip(at + ((at - centre).Normalized() * 12), (at - centre).Normalized(), [corridor.Label, corridor.EntryText],
                labelFont, labelSize, Token(CorridorText.ColorToken(corridor.Quality)));
        }
        if (frame.Ghost?.Landing is { } landed)
        {
            var at = V(projection.Project(landed.Position));
            if (_text is not { } text) return;
            var zone = landed.Band is { } band
                ? text.Format("map.landingZoneBand", ("zone", landed.ZoneLabel), ("band", band))
                : landed.ZoneLabel;
            var word = text.ZoneVerdictWord(landed.Verdict);
            var verdict = landed.Verdict is LandingZoneVerdict.InZone or LandingZoneVerdict.NearEdge ? $"{zone} · {word}" : zone;
            DrawChip(at - ((at - centre).Normalized() * 12), (centre - at).Normalized(),
                [text.Format("map.landingMarker", ("time", landed.TimeText)), landed.CorridorWord + " · " + verdict], labelFont, labelSize,
                Token(CorridorText.ColorToken(landed.Corridor)));
        }
    }

    private void DrawLanding(LandingMarker landing, AltitudeExaggeration projection)
    {
        var position = V(projection.Project(landing.Position));
        var colour = Token(CorridorText.ColorToken(landing.Corridor));
        var arc = _drawList.LandingArc;
        for (var run = 0; run < arc.Count; run++)
            _pathLayer.DrawPolyline(Run(arc, run), Token(CorridorText.ZoneVerdictToken(landing.Verdict)), 3, true);
        const float half = 6;
        _pathLayer.DrawLine(position + new Vector2(-half, -half), position + new Vector2(half, half), colour, 2, true);
        _pathLayer.DrawLine(position + new Vector2(-half, half), position + new Vector2(half, -half), colour, 2, true);
    }

    private void DrawApsis(ApsisMarker apsis, AltitudeExaggeration projection, Vector2 centre, Font font, int fontSize)
    {
        var position = V(projection.Project(apsis.Position));
        _pathLayer.DrawCircle(position, 3, Token("text.hi"));
        if (_frame is not { LabelsVisible: true }) return;
        var outward = (position - centre).Normalized();
        string[] lines = apsis.TimeToText is { } eta ? [apsis.Label, eta] : [apsis.Label];
        DrawChip(position + (outward * 14), outward, lines, font, fontSize, Token("text.hi"));
    }

    private void DrawVehicles(OrbitMapFrame frame, AltitudeExaggeration projection)
    {
        var font = GetThemeFont("font", "Small");
        var fontSize = GetThemeFontSize("font_size", "Small");
        for (var i = 0; i < frame.Vehicles.Count; i++)
        {
            var marker = frame.Vehicles[i];
            var position = V(projection.Project(marker.Position));
            if (!NearView(position)) continue;
            var crewed = i == 0;
            var heading = new Vector2((float)Math.Cos(marker.ScreenRotationRad), (float)Math.Sin(marker.ScreenRotationRad));
            var side = new Vector2(-heading.Y, heading.X);
            var size = crewed ? 8f : 6f;
            Vector2[] icon = [position + (heading * size), position - (heading * size * 0.6f) + (side * size * 0.6f), position - (heading * size * 0.6f) - (side * size * 0.6f)];
            _pathLayer.DrawColoredPolygon(icon, Token(crewed ? "text.hi" : "text.mid"));

            if (crewed && _tracking.AlongTrackUncertaintyM > 0 && _input is { Vehicles.Count: > 0 } input)
            {
                var velocity = MapTransform.DirectionToScreen(input.Vehicles[0].State.VelocityMps);
                var (from, to) = RimGeometry.AlongTrackTick(
                    projection.Project(marker.Position), velocity, frame.Transform.ToPixels(_tracking.AlongTrackUncertaintyM));
                _pathLayer.DrawLine(V(from), V(to), Token("info"), 2);
            }
            if (frame.LabelsVisible)
                _pathLayer.DrawString(font, position + new Vector2(10, 16), VehicleName(marker), HorizontalAlignment.Left, -1, fontSize, Token("text.mid"));
        }
    }

    private void DrawNode(ManeuverNodeView node, AltitudeExaggeration projection)
    {
        var position = V(projection.Project(node.Position));
        if (!NearView(position)) return;
        for (var i = 0; i < 4; i++)
        {
            var handle = node.Handles[i];
            var tip = position + V(handle.Tip - node.Position);
            var colour = Token(handle.Selected ? "accent" : "text.mid");
            _pathLayer.DrawLine(position, tip, colour, handle.Selected ? 2 : 1, true);
            _pathLayer.DrawCircle(tip, 5, Token("bg.1"));
            _pathLayer.DrawArc(tip, 5, 0, Mathf.Tau, 16, colour, 1.5f, true);
        }
        Vector2[] diamond = [position + new Vector2(0, -7), position + new Vector2(7, 0), position + new Vector2(0, 7), position + new Vector2(-7, 0), position + new Vector2(0, -7)];
        _pathLayer.DrawColoredPolygon(diamond[..4], Token("bg.1"));
        _pathLayer.DrawPolyline(diamond, Token("info"), 2, true);
    }

    /// <summary>A label chip: <c>bg.1</c> box with a <c>line.1</c> border, first line in <paramref name="accent"/>, the rest in <c>text.mid</c>.</summary>
    private void DrawChip(Vector2 anchor, Vector2 outward, string[] lines, Font font, int fontSize, Color accent)
    {
        const float padding = 6;
        var lineHeight = fontSize + 3f;
        var width = 0f;
        foreach (var line in lines) width = Math.Max(width, font.GetStringSize(line, HorizontalAlignment.Left, -1, fontSize).X);
        var size = new Vector2(width + (padding * 2), (lineHeight * lines.Length) + padding);
        var origin = anchor + new Vector2(outward.X < -0.3f ? -size.X : outward.X > 0.3f ? 0 : -size.X / 2, outward.Y < -0.3f ? -size.Y : outward.Y > 0.3f ? 0 : -size.Y / 2);
        origin = new Vector2(Mathf.Round(origin.X), Mathf.Round(origin.Y));
        var rect = new Rect2(origin, size);
        _pathLayer.DrawRect(rect, Token("bg.1"));
        _pathLayer.DrawRect(rect, Token("line.1"), false, 1);
        for (var i = 0; i < lines.Length; i++)
        {
            var baseline = origin + new Vector2(padding, (padding / 2) + (lineHeight * i) + fontSize);
            _pathLayer.DrawString(font, baseline, lines[i], HorizontalAlignment.Left, -1, fontSize, i == 0 ? accent : Token("text.mid"));
        }
    }

    // Runs always hold at least two points: Godot rejects shorter polylines (a path can collapse at splashdown).
    private void Polylines(PolylineSet runs, Color colour, float width)
    {
        for (var run = 0; run < runs.Count; run++) _pathLayer.DrawPolyline(Run(runs, run), colour, width, true);
    }

    private void DrawDashes(IReadOnlyList<MapPoint> pairs, Color colour, float width)
    {
        if (pairs.Count >= 2) _pathLayer.DrawMultiline(Points(pairs, 0, pairs.Count & ~1), colour, width, true);
    }

    /// <summary>Convex polygons as triangle fans: never handed to Godot's polygon triangulator.</summary>
    private void FanRuns(CanvasItem layer, PolylineSet polygons, Color colour)
    {
        for (var run = 0; run < polygons.Count; run++)
        {
            var length = polygons.Length(run);
            if (length >= 3) AddTriangles(layer, Fan(length), Run(polygons, run), colour);
        }
    }

    private ReadOnlySpan<int> Fan(int points)
    {
        var count = (points - 2) * 3;
        if (_fan.Length < count) _fan = new int[Math.Max(count, _fan.Length * 2)];
        for (var i = 0; i < points - 2; i++)
        {
            _fan[3 * i] = 0;
            _fan[(3 * i) + 1] = i + 1;
            _fan[(3 * i) + 2] = i + 2;
        }
        return _fan.AsSpan(0, count);
    }

    private ReadOnlySpan<Vector2> Run(PolylineSet runs, int run) => Points(runs.Points, runs.Start(run), runs.Length(run));

    /// <summary>Copies drawn points into the reused buffer; the span is valid until the next call.</summary>
    private ReadOnlySpan<Vector2> Points(IReadOnlyList<MapPoint> points, int start, int count)
    {
        if (_buffer.Length < count) _buffer = new Vector2[Math.Max(count, _buffer.Length * 2)];
        for (var i = 0; i < count; i++) _buffer[i] = V(points[start + i]);
        return _buffer.AsSpan(0, count);
    }

    private ReadOnlySpan<int> Sequence(int count)
    {
        if (_sequence.Length < count)
        {
            _sequence = new int[Math.Max(count, _sequence.Length * 2)];
            for (var i = 0; i < _sequence.Length; i++) _sequence[i] = i;
        }
        return _sequence.AsSpan(0, count);
    }

    private static void DrawDashedCircle(CanvasItem layer, Vector2 centre, float radius, Color colour, float width, float dash, float gap)
    {
        if (radius <= 0) return;
        var circumference = Mathf.Tau * radius;
        var count = Math.Max(8, (int)(circumference / (dash + gap)));
        var step = Mathf.Tau / count;
        var sweep = step * dash / (dash + gap);
        for (var i = 0; i < count; i++) layer.DrawArc(centre, radius, i * step, (i * step) + sweep, 3, colour, width, true);
    }

    private IEnumerable<EntryCorridorBand> Corridors()
    {
        if (_frame?.Corridor is { } current) yield return current;
        if (_frame?.Ghost?.Corridor is { } planned) yield return planned;
    }

    /// <summary>Gate 4 decision 3: primary <c>ok</c>, secondary <c>info</c>, further zones <c>caution</c>; always with words.</summary>
    private static string ZoneToken(int rank) => rank switch
    {
        0 => "ok",
        1 => "info",
        _ => "caution",
    };

    private static string VehicleName(MapVehicleMarker marker) =>
        marker.Label is { Length: > 0 } label ? label
        : marker.VehicleId.Length == 0 ? marker.VehicleId : char.ToUpperInvariant(marker.VehicleId[0]) + marker.VehicleId[1..];

    private static Vector2 Outward(double screenAngleRad) => new((float)Math.Cos(screenAngleRad), (float)Math.Sin(screenAngleRad));

    /// <summary>Markers far off screen are skipped: zoomed in, their coordinates are too large for Godot's float polygons.</summary>
    private bool NearView(Vector2 position) =>
        position.X > -MarkerCullPx && position.Y > -MarkerCullPx && position.X < Size.X + MarkerCullPx && position.Y < Size.Y + MarkerCullPx;

    private static Vector2 V(MapPoint point) => new((float)point.X, (float)point.Y);

    private static MapPoint ToMap(Vector2 point) => new(point.X, point.Y);
}
