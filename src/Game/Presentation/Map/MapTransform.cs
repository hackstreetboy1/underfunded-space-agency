using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// Maps the body's inertial plane (metres, Y up) to the viewport (pixels, Y down). Immutable: zooming and panning
/// return a new transform. Screen angles are the negated world angles, i.e. clockwise-positive like Godot's
/// <c>draw_arc</c> and node rotation.
/// </summary>
/// <param name="CentreM">World point shown at the viewport centre.</param>
public sealed record MapTransform(double ViewportWidthPx, double ViewportHeightPx, Vec2d CentreM, double MetresPerPixel)
{
    public MapPoint ViewportCentre => new(ViewportWidthPx / 2, ViewportHeightPx / 2);

    /// <summary>Distance from <see cref="CentreM"/> to the farthest viewport corner, in metres.</summary>
    public double VisibleRadiusM =>
        Math.Sqrt(ViewportWidthPx * ViewportWidthPx + ViewportHeightPx * ViewportHeightPx) / 2 * MetresPerPixel;

    /// <summary>A transform that shows a circle of <paramref name="radiusM"/> around <paramref name="centreM"/> inside the shorter viewport side.</summary>
    public static MapTransform Fit(double viewportWidthPx, double viewportHeightPx, Vec2d centreM, double radiusM)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportWidthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusM);
        return new MapTransform(viewportWidthPx, viewportHeightPx, centreM, radiusM / (Math.Min(viewportWidthPx, viewportHeightPx) / 2));
    }

    public MapPoint WorldToScreen(Vec2d worldM) => new(
        ViewportWidthPx / 2 + (worldM.X - CentreM.X) / MetresPerPixel,
        ViewportHeightPx / 2 - (worldM.Y - CentreM.Y) / MetresPerPixel);

    public Vec2d ScreenToWorld(MapPoint screen) => new(
        CentreM.X + (screen.X - ViewportWidthPx / 2) * MetresPerPixel,
        CentreM.Y - (screen.Y - ViewportHeightPx / 2) * MetresPerPixel);

    /// <summary>A world direction as a unit screen vector.</summary>
    public static MapPoint DirectionToScreen(Vec2d worldDirection)
    {
        var unit = worldDirection.Normalized();
        return new MapPoint(unit.X, -unit.Y);
    }

    public static double ScreenAngle(double worldAngleRad) => -worldAngleRad;

    public double ToPixels(double metres) => metres / MetresPerPixel;

    public double ToMetres(double pixels) => pixels * MetresPerPixel;

    /// <summary>Zooms by <paramref name="factor"/> (&gt; 1 zooms in) keeping the world point under <paramref name="anchor"/> fixed.</summary>
    public MapTransform ZoomedAt(double factor, MapPoint anchor, double minMetresPerPixel, double maxMetresPerPixel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factor);
        var anchorWorld = ScreenToWorld(anchor);
        var scale = Math.Clamp(MetresPerPixel / factor, minMetresPerPixel, maxMetresPerPixel);
        var centre = new Vec2d(
            anchorWorld.X - (anchor.X - ViewportWidthPx / 2) * scale,
            anchorWorld.Y + (anchor.Y - ViewportHeightPx / 2) * scale);
        return this with { CentreM = centre, MetresPerPixel = scale };
    }

    /// <summary>Moves the content by a pointer drag of (<paramref name="dxPx"/>, <paramref name="dyPx"/>).</summary>
    public MapTransform Panned(double dxPx, double dyPx) =>
        this with { CentreM = new Vec2d(CentreM.X - dxPx * MetresPerPixel, CentreM.Y + dyPx * MetresPerPixel) };

    public MapTransform CentredOn(Vec2d worldM) => this with { CentreM = worldM };

    /// <summary>Keeps the centre and scale; the visible area grows or shrinks with the viewport.</summary>
    public MapTransform Resized(double viewportWidthPx, double viewportHeightPx)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportWidthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeightPx);
        return this with { ViewportWidthPx = viewportWidthPx, ViewportHeightPx = viewportHeightPx };
    }
}
