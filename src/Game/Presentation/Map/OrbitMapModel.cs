using Usa.Game.Content;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// Presentation model of the orbit map (UI.md 4.1): owns the view transform (zoom, pan, focus) and turns the
/// ground's flight picture into screen-space geometry via <see cref="BuildFrame"/>. Sampled paths and zone passes
/// are cached, so building a frame every redraw is cheap while nothing changes.
/// </summary>
public sealed class OrbitMapModel
{
    private const double DefaultViewportPx = 600;

    /// <summary>Length of a station tick outward from the surface.</summary>
    private const double StationTickPx = 8;

    private const string ChipSeparator = " · ";

    private readonly CelestialBody _body;
    private readonly RecoveryZones? _zones;
    private readonly EntryCorridor? _corridor;
    private readonly ZonePassPredictor? _passes;
    private readonly IReadOnlyDictionary<string, int> _zoneRanks;
    private readonly Func<string, string> _zoneLabel;
    private readonly Func<string, string> _stationLabel;
    private readonly MapText _text;

    /// <param name="zones">Recovery zones and the body's rotation; null draws no zones (rotation from the body, θ0 = 0).</param>
    /// <param name="corridor">Entry corridor; null draws no corridor band.</param>
    /// <param name="text">Map words (<c>map.*</c> strings).</param>
    /// <param name="zoneLabel">Rim label for a zone id; defaults to <see cref="RecoveryZoneGeometry.DefaultLabel"/>.</param>
    public OrbitMapModel(
        CelestialBody body, RecoveryZones? zones, EntryCorridor? corridor, ITextSource text, OrbitMapSettings? settings = null,
        Func<string, string>? zoneLabel = null, Func<string, string>? stationLabel = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(text);
        _body = body;
        _text = new MapText(text);
        _zones = zones;
        _corridor = corridor;
        Settings = settings ?? OrbitMapSettings.Default;
        _zoneLabel = zoneLabel ?? RecoveryZoneGeometry.DefaultLabel;
        _stationLabel = stationLabel ?? (id => id);
        _zoneRanks = zones is null ? new Dictionary<string, int>() : RecoveryZoneGeometry.Ranks(zones);
        if (zones is not null) _passes = new ZonePassPredictor(zones, Settings.ZonePassScanStepS, Settings.ZonePassHorizonOrbits);
        Transform = DefaultTransform(DefaultViewportPx, DefaultViewportPx);
    }

    public OrbitMapSettings Settings { get; }

    public MapTransform Transform { get; private set; }

    public bool LabelsVisible { get; set; } = true;

    public ConicPathSampler Sampler { get; } = new();

    public double MinMetresPerPixel => Settings.MinViewRadiusM / (ShortSidePx / 2);

    public double MaxMetresPerPixel => Settings.MaxViewRadiusFactor * _body.RadiusM / (ShortSidePx / 2);

    /// <summary>Sampling tolerance in metres for the current zoom.</summary>
    public double ToleranceM => Settings.SampleTolerancePx * Transform.MetresPerPixel;

    /// <summary>Hyperbolic arms are clipped at this radius: beyond every visible point, rounded up to a power of two.</summary>
    public double ClipRadiusM =>
        Math.Pow(2, Math.Ceiling(Math.Log2(Transform.CentreM.Length + Transform.VisibleRadiusM + _body.RadiusM)));

    private double ShortSidePx => Math.Min(Transform.ViewportWidthPx, Transform.ViewportHeightPx);

    public void SetViewport(double widthPx, double heightPx) => Transform = Transform.Resized(widthPx, heightPx);

    /// <summary>Centre on the body showing <see cref="OrbitMapSettings.DefaultViewRadiusFactor"/> body radii.</summary>
    public void ResetView() => Transform = DefaultTransform(Transform.ViewportWidthPx, Transform.ViewportHeightPx);

    /// <summary>Wheel zoom: <paramref name="factor"/> &gt; 1 zooms in around <paramref name="anchor"/>.</summary>
    public void Zoom(double factor, MapPoint anchor) =>
        Transform = Transform.ZoomedAt(factor, anchor, MinMetresPerPixel, MaxMetresPerPixel);

    /// <summary>Drag pan by a cursor delta in pixels.</summary>
    public void Pan(double dxPx, double dyPx) => Transform = Transform.Panned(dxPx, dyPx);

    /// <summary>"Focus vehicle" (F): centre on <paramref name="positionM"/>, optionally showing <paramref name="viewRadiusM"/> around it.</summary>
    public void Focus(Vec2d positionM, double? viewRadiusM = null)
    {
        Transform = viewRadiusM is { } radius
            ? MapTransform.Fit(Transform.ViewportWidthPx, Transform.ViewportHeightPx, positionM, radius)
            : Transform.CentredOn(positionM);
        var clamped = Math.Clamp(Transform.MetresPerPixel, MinMetresPerPixel, MaxMetresPerPixel);
        Transform = Transform with { MetresPerPixel = clamped };
    }

    public ConicArc ArcOf(in Conic conic) => ConicArc.Of(conic, _body.RadiusM, ClipRadiusM);

    /// <summary>Anomaly and time under the cursor while dragging the maneuver node along <paramref name="conic"/>.</summary>
    public (double TrueAnomalyRad, MissionTime? Time) ProjectPointer(in Conic conic, MapPoint cursor, MissionTime now) =>
        ManeuverNodeView.ProjectPointer(conic, ArcOf(conic), Transform, cursor, now);

    public EarthDisc Earth(MissionTime time)
    {
        var rotation = _zones?.RotationAngleRad(time) ?? _body.RotationRateRadPerS * time.Seconds;
        return new EarthDisc(
            Transform.WorldToScreen(Vec2d.Zero), Transform.ToPixels(_body.RadiusM),
            Transform.ToPixels(_body.RadiusM + _body.InterfaceAltitudeM), Angles.Wrap(rotation),
            MapTransform.ScreenAngle(Angles.Wrap(rotation)), time);
    }

    /// <summary>Recovery-zone arcs at <paramref name="time"/>, with next-pass ETAs along <paramref name="conic"/> when given.</summary>
    public IReadOnlyList<RecoveryZoneArc> ZoneArcs(MissionTime time, Conic? conic = null)
    {
        if (_zones is null) return [];
        MissionTime? until = null;
        if (conic is { } path && ArcOf(path) is { End: ArcEnd.Impact } impactArc)
            until = path.NextTimeAtTrueAnomaly(Angles.Wrap(impactArc.EndAnomalyRad), time);

        var rim = _body.RadiusM + Transform.ToMetres(Settings.ZoneRimOffsetPx);
        var arcs = new List<RecoveryZoneArc>(_zones.Zones.Count);
        foreach (var zone in _zones.Zones)
        {
            var next = conic is { } c && _passes is not null ? _passes.NextPass(c, zone, time, until) : null;
            arcs.Add(RecoveryZoneGeometry.Arc(
                _zones, zone, time, Transform, rim, Settings.ZoneArcStepRad, _zoneLabel(zone.Id), _zoneRanks[zone.Id], next, _text));
        }
        return arcs;
    }

    public OrbitMapFrame BuildFrame(OrbitMapInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = input.Now;
        MapPolyline? currentPath = null;
        var pathEnd = ArcEnd.Empty;
        IReadOnlyList<ApsisMarker> apsides = [];
        EntryCorridorBand? corridor = null;
        UncertaintyBand? uncertainty = null;
        ManeuverNodeView? node = null;
        OrbitChip? chip = null;
        double? corridorPeriapsisKm = null;

        if (input.CurrentConic is { } conic)
        {
            chip = Chip(conic, input.Orbit, now);
            if (_corridor is not null) corridorPeriapsisKm = CorridorPeriapsis.AltitudeKm(conic, _body.RadiusM, _corridor);
            var arc = ArcOf(conic);
            pathEnd = arc.End;
            var sampled = Sampler.Sample(conic, arc, ToleranceM);
            var pointsM = arc.IsClosed ? sampled.PointsM : sampled.From(ConicGeometry.AnomalyAt(conic, now));
            if (!arc.IsEmpty) currentPath = new MapPolyline(ToScreen(pointsM), arc.IsClosed, MapStyleKeys.CurrentPath);
            apsides = ApsisMarker.For(conic, arc, _body.RadiusM, Transform, now, Settings.MinApsisEccentricity, _text, input.Orbit);
            if (_corridor is not null)
                corridor = EntryCorridorBand.For(conic, arc, _body.RadiusM, _corridor, Transform, Sampler, ToleranceM, Settings.CorridorBottomAltitudeM, _text);
            if (currentPath is not null && input.NavUncertaintyM > 0)
            {
                var halfWidth = Transform.ToPixels(input.NavUncertaintyM);
                uncertainty = UncertaintyBand.Around(currentPath.Points, currentPath.Closed, halfWidth, halfWidth * input.UncertaintyGrowth);
            }
            if (input.NodeTime is { } nodeTime)
                node = ManeuverNodeView.At(conic, nodeTime, now, Transform, Settings.HandleLengthPx, input.NodeDirection, input.NodeCustomAngleDeg, _text);
        }

        var ghost = input.Preview is { } preview ? Ghost(preview, now) : null;

        var vehicles = new MapVehicleMarker[input.Vehicles.Count];
        for (var i = 0; i < vehicles.Length; i++) vehicles[i] = MapVehicleMarker.For(input.Vehicles[i], Transform);

        return new OrbitMapFrame(
            Transform, Earth(now), ZoneArcs(now, input.CurrentConic), currentPath, pathEnd, apsides, corridor, uncertainty,
            node, ghost, vehicles, LabelsVisible, StationTicks(input.Stations, now),
            input.NextFixAt is { } fixAt ? _text.NextFix(now, fixAt) : null,
            chip,
            corridorPeriapsisKm);
    }

    /// <summary>The orbit chip from the estimated conic: true apsis altitudes even when the markers are hidden (near-circular).</summary>
    private OrbitChip Chip(in Conic conic, OrbitFix? orbit, MissionTime now)
    {
        var periapsis = _text.Apsis(ApsisKind.Periapsis, (conic.PeriapsisM - _body.RadiusM) / 1000);
        var apoapsis = conic.IsHyperbolic
            ? _text.Get("map.apoapsisEscape")
            : _text.Apsis(ApsisKind.Apoapsis, (conic.ApoapsisM - _body.RadiusM) / 1000);
        var apsides = periapsis + ChipSeparator + apoapsis;
        double? periodS = conic.IsHyperbolic || !double.IsFinite(conic.PeriodS) ? null : conic.PeriodS;
        var period = periodS is { } seconds ? _text.Period(seconds) : null;
        return new OrbitChip(
            apsides, periodS, period, period is null ? apsides : apsides + ChipSeparator + period,
            orbit is null ? null : _text.OrbitMeta(orbit, now),
            orbit?.StateAt(now) ?? OrbitFixState.None,
            orbit is null ? string.Empty : _text.OrbitBadge(orbit, now));
    }

    private GhostPath Ghost(ManeuverPreview preview, MissionTime now)
    {
        var ghost = GhostPath.For(preview, _body.RadiusM, Transform, Sampler, ToleranceM, ClipRadiusM, now, _zoneLabel, _text);

        EntryCorridorBand? corridor = null;
        // A burn that ends below the interface has already crossed it; the band would sit behind the ghost.
        if (_corridor is not null && preview.Entry is { } entry && entry.InterfaceTime >= preview.Plan.EndTime)
        {
            var conic = preview.ResultingConic;
            corridor = EntryCorridorBand.For(
                conic, ArcOf(conic), _body.RadiusM, _corridor, Transform, Sampler, ToleranceM, Settings.CorridorBottomAltitudeM, _text);
        }

        RecoveryZoneArc? zoneAtLanding = null;
        if (_zones is not null && ghost.Landing is { ZoneId: { } zoneId } landing)
        {
            foreach (var zone in _zones.Zones)
            {
                if (zone.Id != zoneId) continue;
                var rim = _body.RadiusM + Transform.ToMetres(Settings.ZoneRimOffsetPx);
                zoneAtLanding = RecoveryZoneGeometry.Arc(
                    _zones, zone, landing.Time, Transform, rim, Settings.ZoneArcStepRad, _zoneLabel(zone.Id), _zoneRanks[zone.Id], null, _text);
                break;
            }
        }

        return corridor is null && zoneAtLanding is null ? ghost : ghost with { Corridor = corridor, ZoneAtLanding = zoneAtLanding };
    }

    private StationTick[] StationTicks(IReadOnlyList<TrackingStation>? stations, MissionTime time)
    {
        if (stations is null || stations.Count == 0) return [];
        var rotation = _zones?.RotationAngleRad(time) ?? _body.RotationRateRadPerS * time.Seconds;
        var outerM = _body.RadiusM + Transform.ToMetres(StationTickPx);
        var ticks = new StationTick[stations.Count];
        for (var i = 0; i < ticks.Length; i++)
        {
            var station = stations[i];
            var angle = rotation + station.LongitudeDeg * Angles.DegToRad;
            ticks[i] = new StationTick(station.Id, _stationLabel(station.Id),
                Transform.WorldToScreen(Vec2d.FromPolar(_body.RadiusM, angle)), Transform.WorldToScreen(Vec2d.FromPolar(outerM, angle)),
                station.LongitudeDeg);
        }
        return ticks;
    }

    private MapTransform DefaultTransform(double widthPx, double heightPx) =>
        MapTransform.Fit(widthPx, heightPx, Vec2d.Zero, Settings.DefaultViewRadiusFactor * _body.RadiusM);

    private MapPoint[] ToScreen(IReadOnlyList<Vec2d> pointsM)
    {
        var points = new MapPoint[pointsM.Count];
        for (var i = 0; i < points.Length; i++) points[i] = Transform.WorldToScreen(pointsM[i]);
        return points;
    }
}
