namespace Usa.Game.Presentation.Map;

/// <summary>A screen-space polyline ready for <c>draw_polyline</c>.</summary>
/// <param name="Closed">The last point repeats the first (a full orbit).</param>
/// <param name="StyleKey">One of <see cref="MapStyleKeys"/>.</param>
public sealed record MapPolyline(IReadOnlyList<MapPoint> Points, bool Closed, string StyleKey);
