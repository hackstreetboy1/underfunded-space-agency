using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>Places recovery zones on the rotating body's rim: a zone at longitude λ sits at world angle λ + θ(t).</summary>
public static class RecoveryZoneGeometry
{
    /// <summary>A rim label from a zone id: <c>secondary_west_pacific</c> → <c>SECONDARY WEST PACIFIC</c>.</summary>
    public static string DefaultLabel(string zoneId)
    {
        ArgumentNullException.ThrowIfNull(zoneId);
        return zoneId.Replace('_', ' ').ToUpperInvariant();
    }

    /// <summary>
    /// Rim labels from the mission's zone display names (debrief.json <c>zoneNames</c>, text audit L14):
    /// <c>the secondary West Pacific zone</c> → <c>SECONDARY WEST PACIFIC ZONE</c>. An id without a name falls back to
    /// <see cref="DefaultLabel"/>.
    /// </summary>
    public static Func<string, string> Labels(IReadOnlyDictionary<string, string> zoneNames)
    {
        ArgumentNullException.ThrowIfNull(zoneNames);
        const string Article = "the ";
        return zoneId =>
        {
            if (!zoneNames.TryGetValue(zoneId, out var name) || string.IsNullOrWhiteSpace(name)) return DefaultLabel(zoneId);
            var trimmed = name.Trim();
            if (trimmed.StartsWith(Article, StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[Article.Length..];
            return trimmed.ToUpperInvariant();
        };
    }

    /// <summary>World angles (west edge, east edge) of <paramref name="zone"/> at <paramref name="time"/>; east = west + width.</summary>
    public static (double WestRad, double EastRad) WorldAngles(RecoveryZones zones, RecoveryZone zone, MissionTime time)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(zone);
        var west = Angles.Wrap(zone.WestLongitudeDeg * Angles.DegToRad + zones.RotationAngleRad(time));
        return (west, west + zone.WidthRad);
    }

    /// <summary>Wait-time rank of each zone (0 = shortest wait), ties broken by id.</summary>
    public static IReadOnlyDictionary<string, int> Ranks(RecoveryZones zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        var ordered = zones.Zones.OrderBy(z => z.WaitHours).ThenBy(z => z.Id, StringComparer.Ordinal).ToArray();
        var ranks = new Dictionary<string, int>(ordered.Length, StringComparer.Ordinal);
        for (var i = 0; i < ordered.Length; i++) ranks[ordered[i].Id] = i;
        return ranks;
    }

    public static RecoveryZoneArc Arc(
        RecoveryZones zones, RecoveryZone zone, MissionTime time, MapTransform transform, double rimRadiusM,
        double stepRad, string label, int rank, MissionTime? nextPass, MapText text)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(text);
        var (west, east) = WorldAngles(zones, zone, time);
        var steps = Math.Max(2, (int)Math.Ceiling((east - west) / stepRad));
        var points = new MapPoint[steps + 1];
        for (var i = 0; i <= steps; i++) points[i] = transform.WorldToScreen(Vec2d.FromPolar(rimRadiusM, west + (east - west) * i / steps));
        var anchor = transform.WorldToScreen(Vec2d.FromPolar(rimRadiusM + transform.ToMetres(14), (west + east) / 2));
        var hours = ("hours", FlightFormat.Hours(zone.WaitHours));
        var eta = nextPass is { } pass ? text.Relative(time, pass) : null;
        var detail = eta is null
            ? text.Format("map.zoneRecovery", hours)
            : text.Format("map.zonePassRecovery", ("eta", eta), hours);
        return new RecoveryZoneArc(
            zone.Id, label, west, east, MapTransform.ScreenAngle(west), MapTransform.ScreenAngle(east),
            transform.ToPixels(rimRadiusM), points, anchor, zone.WaitHours, rank, nextPass,
            eta ?? FlightFormat.Placeholder, detail);
    }
}
