using Usa.Game.Presentation.Map;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>
/// Wheel zoom about the cursor in drawn space. The model zooms about a true-scale point, but the map draws through the
/// ×11 altitude projection, whose scale changes with zoom: the world point under the cursor would slide away within a few
/// notches. After zooming, the view is panned so that point is drawn under the cursor again (a pan moves every drawn
/// point by the same amount, so one correction is exact). At the zoom limits the scale is clamped and the point stays put.
/// </summary>
public static class CursorZoom
{
    /// <param name="cursorPx">The cursor on the canvas, in drawn pixels.</param>
    /// <param name="now">Time of the shown frame (the body's drawn disc).</param>
    public static void Apply(OrbitMapModel model, double factor, MapPoint cursorPx, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(model);
        var before = AltitudeExaggeration.For(model.Earth(now));
        var anchor = before.Unproject(cursorPx);
        var world = model.Transform.ScreenToWorld(anchor);
        model.Zoom(factor, anchor);
        var after = AltitudeExaggeration.For(model.Earth(now));
        var drawn = after.Project(model.Transform.WorldToScreen(world));
        model.Pan(cursorPx.X - drawn.X, cursorPx.Y - drawn.Y);
    }
}
