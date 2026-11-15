using Usa.Game.Presentation.Map;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>
/// The orbit map's drawn geometry for one frame, in drawn (×11 altitude) screen pixels, clipped to the viewport and
/// bounded at any zoom: the canvas builds it once per rebuild (new input, zoom, pan, resize) and its draw callbacks only
/// emit it. Paths become visible runs of at most <see cref="MaxRunPoints"/> points, dashes are kept only where they can
/// be seen (in the same phase as the whole path), circles larger than <see cref="LargeCirclePx"/> become the visible
/// part of the disc or arc sampled to <see cref="CircleTolerancePx"/>. At the default zoom nothing is clipped and the
/// result matches drawing the frame whole. Buffers are reused between builds.
/// </summary>
public sealed class OrbitMapDrawList
{
    /// <summary>Clip margin beyond the viewport: wider than any stroke, so clipped ends and joins are never seen.</summary>
    public const double MarginPx = 32;

    /// <summary>Godot fills a circle with 64 segments: above this radius the chords would show, so the disc is drawn as a clipped polygon.</summary>
    public const double LargeCirclePx = 512;

    public const double CircleTolerancePx = 0.25;

    public const int MaxRunPoints = 512;

    public const int MaxDashes = 4096;

    public const int MaxBandTriangles = 4096;

    public const double AtmosphereHaloPx = 4;

    /// <summary>Zone arcs sit this far outside the drawn surface.</summary>
    public const double ZoneGapPx = 4;

    /// <summary>Points per zone arc at normal zoom (the former <c>draw_arc</c> count).</summary>
    public const int ZoneArcPoints = 32;

    public const double InterfaceDashPx = 2;
    public const double InterfaceGapPx = 4;
    public const double ZoneAtLandingOffsetPx = 6;
    public const double ZoneAtLandingDashPx = 5;
    public const double ZoneAtLandingGapPx = 3;
    public const double CoastDashPx = 6;
    public const double CoastGapPx = 4;
    public const double DescentDashPx = 2;
    public const double DescentGapPx = 3;

    /// <summary>The landing dispersion arc is drawn just outside the zone arcs so both stay readable.</summary>
    public const double LandingArcOffsetPx = 10;

    /// <summary>Angular steps per land patch: each step is one convex annular-sector quad.</summary>
    public const int LandSteps = 12;

    /// <summary>Cap on fill triangles per layer (land, corridor).</summary>
    public const int MaxFillTriangles = 4096;

    private const double TwoPi = 2 * Math.PI;

    /// <summary>Stylised land silhouette: (west, east longitude°, inner, outer radius as a fraction of the drawn disc).</summary>
    private static readonly (double West, double East, double Inner, double Outer)[] LandPatches =
    [
        (-125, -65, 0.45, 0.92), (-80, -38, 0.18, 0.55), (-15, 50, 0.32, 0.94), (60, 145, 0.25, 0.8), (113, 153, 0.78, 0.94),
    ];

    private readonly List<MapPoint> _scratch = [];
    private readonly List<MapPoint> _clipScratch = [];
    private readonly List<(double From, double To)> _spans = [];
    private readonly List<double> _interfaceDashAngles = [];
    private readonly List<int> _zoneArcRanks = [];
    private readonly List<MapPoint> _bandTriangles = [];
    private readonly List<MapPoint> _zoneAtLandingDashes = [];
    private readonly List<MapPoint> _coastDashes = [];
    private readonly List<MapPoint> _descentDashes = [];
    private readonly List<MapPoint> _fill = [];
    private readonly List<MapPoint> _edge = [];
    private readonly List<MapPoint> _landTriangles = [];
    private readonly List<MapPoint> _corridorTriangles = [];

    public ClipRect Clip { get; private set; }

    public MapPoint Centre { get; private set; }

    public double BodyRadiusPx { get; private set; }

    public double HaloRadiusPx { get; private set; }

    /// <summary>Radius of the recovery-zone arcs (drawn surface + <see cref="ZoneGapPx"/>).</summary>
    public double RimRadiusPx { get; private set; }

    public double InterfaceRadiusPx { get; private set; }

    /// <summary>True when the halo and body are drawn from <see cref="Halo"/> and <see cref="Body"/>; false: plain circles of their radii.</summary>
    public bool DiscsAsPolygons { get; private set; }

    /// <summary>The visible part of the atmosphere halo disc (one convex polygon, or none).</summary>
    public PolylineSet Halo { get; } = new();

    /// <summary>The visible part of the body disc (one convex polygon, or none).</summary>
    public PolylineSet Body { get; } = new();

    /// <summary>Start angles of the visible entry-interface ring dashes, each <see cref="InterfaceDashSweepRad"/> long.</summary>
    public IReadOnlyList<double> InterfaceDashAngles => _interfaceDashAngles;

    public double InterfaceDashSweepRad { get; private set; }

    /// <summary>Nav-uncertainty fill (current path and ghost coast), every three points one triangle.</summary>
    public IReadOnlyList<MapPoint> BandTriangles => _bandTriangles;

    /// <summary>The visible land patches, clipped to the viewport: every three points one triangle.</summary>
    public IReadOnlyList<MapPoint> LandTriangles => _landTriangles;

    /// <summary>The visible entry corridor fills (current, then planned), clipped: every three points one triangle.</summary>
    public IReadOnlyList<MapPoint> CorridorTriangles => _corridorTriangles;

    public PolylineSet CorridorEdges { get; } = new();

    public PolylineSet CurrentPath { get; } = new();

    /// <summary>Recovery-zone arcs; run <c>i</c> takes the colour of <see cref="ZoneArcRanks"/>[i].</summary>
    public PolylineSet ZoneArcs { get; } = new();

    public IReadOnlyList<int> ZoneArcRanks => _zoneArcRanks;

    /// <summary>The landing zone at splashdown, dashed: point pairs.</summary>
    public IReadOnlyList<MapPoint> ZoneAtLandingDashes => _zoneAtLandingDashes;

    public IReadOnlyList<MapPoint> CoastDashes => _coastDashes;

    public IReadOnlyList<MapPoint> DescentDashes => _descentDashes;

    public PolylineSet LandingArc { get; } = new();

    /// <summary>Every point handed to the canvas this frame.</summary>
    public int PointCount =>
        Halo.PointCount + Body.PointCount + (_interfaceDashAngles.Count * 3) + _bandTriangles.Count + _landTriangles.Count + _corridorTriangles.Count
        + CorridorEdges.PointCount + CurrentPath.PointCount + ZoneArcs.PointCount + _zoneAtLandingDashes.Count
        + _coastDashes.Count + _descentDashes.Count + LandingArc.PointCount;

    /// <summary>Points in the largest single polyline or polygon.</summary>
    public int LargestRun => Math.Max(
        Math.Max(Math.Max(Halo.MaxRunLength, Body.MaxRunLength), CorridorEdges.MaxRunLength),
        Math.Max(Math.Max(CurrentPath.MaxRunLength, ZoneArcs.MaxRunLength), LandingArc.MaxRunLength));

    public void Build(OrbitMapFrame frame, AltitudeExaggeration projection, OrbitMapInput? input, double widthPx, double heightPx)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(projection);
        Clear();
        Clip = ClipRect.Viewport(widthPx, heightPx, MarginPx);
        Centre = frame.Earth.Centre;
        BodyRadiusPx = projection.DrawnBodyRadiusPx;
        HaloRadiusPx = BodyRadiusPx + AtmosphereHaloPx;
        RimRadiusPx = BodyRadiusPx + ZoneGapPx;
        InterfaceRadiusPx = projection.ProjectRadius(frame.Earth.AtmosphereRadiusPx);
        DiscsAsPolygons = HaloRadiusPx > LargeCirclePx;
        if (DiscsAsPolygons)
        {
            Disc(HaloRadiusPx, Halo);
            Disc(BodyRadiusPx, Body);
        }
        Land(frame.Earth.ScreenRotationRad);
        InterfaceDashes();
        Bands(frame, projection, input);
        Corridors(frame, projection);
        Zones(frame);
        if (frame.Ghost?.ZoneAtLanding is { } atLanding) ZoneAtLanding(atLanding);
        if (frame.CurrentPath is { } current)
        {
            ProjectInto(current.Points, projection);
            ViewportClip.Polyline(_scratch, Clip, CurrentPath, MaxRunPoints);
        }
        if (frame.Ghost is { } ghost) Ghost(ghost, projection);
    }

    private void Clear()
    {
        Halo.Clear();
        Body.Clear();
        _interfaceDashAngles.Clear();
        _bandTriangles.Clear();
        _landTriangles.Clear();
        _corridorTriangles.Clear();
        CorridorEdges.Clear();
        CurrentPath.Clear();
        ZoneArcs.Clear();
        _zoneArcRanks.Clear();
        _zoneAtLandingDashes.Clear();
        _coastDashes.Clear();
        _descentDashes.Clear();
        LandingArc.Clear();
    }

    private void Disc(double radiusPx, PolylineSet output)
    {
        var coverage = ViewportClip.Coverage(Centre, radiusPx, Clip);
        if (coverage == CircleCoverage.None) return;
        if (coverage == CircleCoverage.Encloses)
        {
            output.Begin(3);
            output.Add(new MapPoint(Clip.MinX, Clip.MinY));
            output.Add(new MapPoint(Clip.MaxX, Clip.MinY));
            output.Add(new MapPoint(Clip.MaxX, Clip.MaxY));
            output.Add(new MapPoint(Clip.MinX, Clip.MaxY));
            output.End(3);
            return;
        }
        _scratch.Clear();
        var wedge = ViewportClip.Wedge(Centre, Clip);
        ViewportClip.AppendArc(Centre, radiusPx, wedge.Start, wedge.End, CircleTolerancePx, MaxRunPoints - 1, _scratch);
        // A sector narrower than π is convex, as is the whole disc, so the rectangle clip stays a simple convex polygon.
        if (wedge.End - wedge.Start >= TwoPi) _scratch.RemoveAt(_scratch.Count - 1);
        else _scratch.Add(Centre);
        ViewportClip.ClipConvexPolygon(_scratch, Clip, _clipScratch);
        if (!ViewportClip.Simplify(_scratch)) return;
        output.Begin(3);
        foreach (var point in _scratch) output.Add(point);
        output.End(3);
    }

    /// <summary>Each land patch as convex annular-sector quads (the same outline as one polygon), clipped to the viewport.</summary>
    private void Land(double rotationRad)
    {
        if (ViewportClip.Coverage(Centre, BodyRadiusPx, Clip) == CircleCoverage.None) return;
        foreach (var (west, east, inner, outer) in LandPatches)
        {
            for (var i = 0; i < LandSteps && _landTriangles.Count < 3 * MaxFillTriangles; i++)
            {
                var from = rotationRad - ((west + ((east - west) * i / LandSteps)) * Math.PI / 180);
                var to = rotationRad - ((west + ((east - west) * (i + 1) / LandSteps)) * Math.PI / 180);
                _fill.Clear();
                _fill.Add(OnRim(BodyRadiusPx * outer, from));
                _fill.Add(OnRim(BodyRadiusPx * outer, to));
                _fill.Add(OnRim(BodyRadiusPx * inner, to));
                _fill.Add(OnRim(BodyRadiusPx * inner, from));
                if (Overlaps(_fill)) ViewportClip.AppendConvexFill(_fill, Clip, _clipScratch, _landTriangles);
            }
        }
    }

    private void InterfaceDashes()
    {
        var radius = InterfaceRadiusPx;
        if (ViewportClip.Coverage(Centre, radius, Clip) != CircleCoverage.Crosses) return;
        var count = Math.Max(8, (int)(TwoPi * radius / (InterfaceDashPx + InterfaceGapPx)));
        var step = TwoPi / count;
        InterfaceDashSweepRad = step * InterfaceDashPx / (InterfaceDashPx + InterfaceGapPx);
        var wedge = ViewportClip.Wedge(Centre, Clip);
        var full = wedge.End - wedge.Start >= TwoPi;
        var first = full ? 0 : (long)Math.Floor((wedge.Start - InterfaceDashSweepRad) / step);
        var last = full ? count - 1 : (long)Math.Floor(wedge.End / step);
        for (var k = first; k <= last && _interfaceDashAngles.Count < MaxDashes; k++) _interfaceDashAngles.Add(k * step);
    }

    private void Bands(OrbitMapFrame frame, AltitudeExaggeration projection, OrbitMapInput? input)
    {
        if (frame.CurrentPath is { } current && frame.Uncertainty is { } band)
        {
            ProjectInto(current.Points, projection);
            AddBand(current.Closed, band.StartHalfWidthPx, band.EndHalfWidthPx, projection);
        }
        if (frame.Ghost?.Coast is { } coast && input is { NavUncertaintyM: > 0 })
        {
            var start = frame.Transform.ToPixels(input.NavUncertaintyM);
            ProjectInto(coast.Points, projection);
            AddBand(coast.Closed, start, start * Math.Max(1, input.UncertaintyGrowth), projection);
        }
    }

    private void AddBand(bool closed, double startHalfWidthPx, double endHalfWidthPx, AltitudeExaggeration projection)
    {
        var room = MaxBandTriangles - (_bandTriangles.Count / 3);
        if (room <= 0) return;
        ExaggeratedBand.AppendVisibleTriangles(_scratch, closed, startHalfWidthPx, endHalfWidthPx, projection, Clip, _bandTriangles, room);
    }

    private void Corridors(OrbitMapFrame frame, AltitudeExaggeration projection)
    {
        if (frame.Corridor is { } current) Corridor(current, projection);
        if (frame.Ghost?.Corridor is { } planned) Corridor(planned, projection);
    }

    private void Corridor(EntryCorridorBand corridor, AltitudeExaggeration projection)
    {
        ProjectInto(corridor.Polygon, projection);
        if (_scratch.Count >= 3 && Overlaps(_scratch)) CorridorFill(corridor, projection);
        ProjectInto(corridor.SteepEdge, projection);
        ViewportClip.Polyline(_scratch, Clip, CorridorEdges, MaxRunPoints);
        ProjectInto(corridor.ShallowEdge, projection);
        ViewportClip.Polyline(_scratch, Clip, CorridorEdges, MaxRunPoints);
    }

    /// <summary>
    /// The band between the steep and shallow edges (which leave the same interface point) as a strip of triangles,
    /// stepping along whichever edge gives the shorter diagonal; each triangle is clipped to the viewport.
    /// </summary>
    private void CorridorFill(EntryCorridorBand corridor, AltitudeExaggeration projection)
    {
        ProjectInto(corridor.SteepEdge, projection);
        _edge.Clear();
        for (var k = 0; k < corridor.ShallowEdge.Count; k++) _edge.Add(projection.Project(corridor.ShallowEdge[k]));
        if (_scratch.Count == 0 || _edge.Count == 0) return;
        int i = 0, j = 0;
        while ((i < _scratch.Count - 1 || j < _edge.Count - 1) && _corridorTriangles.Count < 3 * MaxFillTriangles)
        {
            var steep = _scratch[i];
            var shallow = _edge[j];
            var alongSteep = j == _edge.Count - 1
                || (i < _scratch.Count - 1 && _scratch[i + 1].DistanceTo(shallow) <= steep.DistanceTo(_edge[j + 1]));
            var next = alongSteep ? _scratch[++i] : _edge[++j];
            _fill.Clear();
            _fill.Add(steep);
            _fill.Add(shallow);
            _fill.Add(next);
            if (Overlaps(_fill)) ViewportClip.AppendConvexFill(_fill, Clip, _clipScratch, _corridorTriangles);
        }
    }

    private void Zones(OrbitMapFrame frame)
    {
        foreach (var zone in frame.Zones)
        {
            var before = ZoneArcs.Count;
            if (RimRadiusPx <= LargeCirclePx)
            {
                _scratch.Clear();
                var delta = Math.Clamp(zone.EndScreenAngleRad - zone.StartScreenAngleRad, -TwoPi, TwoPi);
                for (var i = 0; i < ZoneArcPoints; i++)
                {
                    var angle = zone.StartScreenAngleRad + (delta * i / (ZoneArcPoints - 1));
                    _scratch.Add(OnRim(RimRadiusPx, angle));
                }
                ViewportClip.Polyline(_scratch, Clip, ZoneArcs, MaxRunPoints);
            }
            else
            {
                ViewportClip.ArcSpans(zone.StartScreenAngleRad, zone.EndScreenAngleRad, ViewportClip.Wedge(Centre, Clip), _spans);
                foreach (var (from, to) in _spans)
                {
                    _scratch.Clear();
                    ViewportClip.AppendArc(Centre, RimRadiusPx, from, to, CircleTolerancePx, MaxRunPoints, _scratch);
                    ViewportClip.Polyline(_scratch, Clip, ZoneArcs, MaxRunPoints);
                }
            }
            for (var i = before; i < ZoneArcs.Count; i++) _zoneArcRanks.Add(zone.Rank);
        }
    }

    private void ZoneAtLanding(RecoveryZoneArc zone)
    {
        var radius = RimRadiusPx + ZoneAtLandingOffsetPx;
        var start = zone.StartScreenAngleRad;
        var end = zone.EndScreenAngleRad;
        if (radius <= LargeCirclePx)
        {
            // The former fixed 0.02 rad sampling, kept so the dash pattern is unchanged at normal zoom.
            var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(end - start) / 0.02));
            _scratch.Clear();
            for (var i = 0; i <= steps; i++) _scratch.Add(OnRim(radius, start + ((end - start) * i / steps)));
            ViewportClip.Dashes(_scratch, ZoneAtLandingDashPx, ZoneAtLandingGapPx, Clip, _zoneAtLandingDashes, MaxDashes);
            return;
        }
        ViewportClip.ArcSpans(start, end, ViewportClip.Wedge(Centre, Clip), _spans);
        foreach (var (from, to) in _spans)
        {
            _scratch.Clear();
            ViewportClip.AppendArc(Centre, radius, from, to, CircleTolerancePx, MaxRunPoints, _scratch);
            ViewportClip.Dashes(_scratch, ZoneAtLandingDashPx, ZoneAtLandingGapPx, Clip, _zoneAtLandingDashes, MaxDashes,
                radius * Math.Abs(from - start));
        }
    }

    private void Ghost(GhostPath ghost, AltitudeExaggeration projection)
    {
        if (ghost.Coast is { } coast)
        {
            ProjectInto(coast.Points, projection);
            ViewportClip.Dashes(_scratch, CoastDashPx, CoastGapPx, Clip, _coastDashes, MaxDashes);
        }
        if (ghost.Descent is { } descent)
        {
            ProjectInto(descent.Points, projection);
            ViewportClip.Dashes(_scratch, DescentDashPx, DescentGapPx, Clip, _descentDashes, MaxDashes);
        }
        if (ghost.Landing is not null && ghost.LandingArc is { Points.Count: >= 2 } arc)
        {
            _scratch.Clear();
            var radius = RimRadiusPx + LandingArcOffsetPx;
            foreach (var point in arc.Points) _scratch.Add(Centre + ((point - Centre).Normalized() * radius));
            ViewportClip.Polyline(_scratch, Clip, LandingArc, MaxRunPoints);
        }
    }

    private void ProjectInto(IReadOnlyList<MapPoint> points, AltitudeExaggeration projection)
    {
        _scratch.Clear();
        for (var i = 0; i < points.Count; i++) _scratch.Add(projection.Project(points[i]));
    }

    private bool Overlaps(List<MapPoint> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }
        return Clip.Overlaps(minX, minY, maxX, maxY);
    }

    private MapPoint OnRim(double radiusPx, double screenAngleRad) =>
        Centre + new MapPoint(Math.Cos(screenAngleRad) * radiusPx, Math.Sin(screenAngleRad) * radiusPx);
}
