using System.Globalization;
using Usa.Game.Content;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// Words on the orbit map and in the burn planner, from the <c>map.*</c> and <c>planner.*</c> strings; numbers and
/// units come from <see cref="FlightFormat"/>.
/// </summary>
public sealed class MapText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public MapText(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Source = text;
    }

    public ITextSource Source { get; }

    public string OutsideZones => Source.Get("map.outsideZones");

    /// <summary>
    /// <c>in 12 min</c>, <c>in 2 h 5 min</c>; <c>now</c> when the time has come. Same span rule as the console
    /// readouts (whole seconds, truncated); the placeholder for a non-finite span.
    /// </summary>
    public string Relative(double seconds)
    {
        if (double.IsNaN(seconds)) return FlightFormat.Placeholder;
        if (seconds < 1) return Source.Get("map.relativeNow");
        if (double.IsPositiveInfinity(seconds)) return FlightFormat.Placeholder;
        var totalSeconds = (long)seconds;
        var minutes = totalSeconds / 60 % 60;
        var time = totalSeconds < 60 ? string.Create(Invariant, $"{totalSeconds} s")
            : totalSeconds < 3600 ? string.Create(Invariant, $"{totalSeconds / 60} min")
            : minutes == 0 ? string.Create(Invariant, $"{totalSeconds / 3600} h")
            : string.Create(Invariant, $"{totalSeconds / 3600} h {minutes} min");
        return Source.Format("map.relativeIn", ("time", time));
    }

    public string Relative(MissionTime now, MissionTime then) => Relative((then.Milliseconds - now.Milliseconds) / 1000.0);

    /// <summary><c>MET 06:20:00</c>; signed before lift-off (<c>MET −00:00:01</c>).</summary>
    public string Met(MissionTime time) => Source.Format("map.met", ("time", FlightFormat.Met(time)));

    public string CorridorWord(CorridorQuality quality) => Source.Get(quality switch
    {
        CorridorQuality.Ok => "map.corridorOk",
        CorridorQuality.TooSteep => "map.corridorTooSteep",
        CorridorQuality.TooShallow => "map.corridorTooShallow",
        _ => "map.corridorNoEntry",
    });

    /// <summary><c>Pe 290 km</c> or <c>Ap 305 km</c>.</summary>
    public string Apsis(ApsisKind kind, double altitudeKm) =>
        Source.Format(kind == ApsisKind.Periapsis ? "map.periapsis" : "map.apoapsis", ("altitude", FlightFormat.AltitudeKm(altitudeKm)));

    /// <summary><c>ground tracking · MET 06:58</c>, or <c>EST · ground tracking · MET 06:58</c> for an estimated orbit.</summary>
    public string OrbitMeta(OrbitFix orbit)
    {
        ArgumentNullException.ThrowIfNull(orbit);
        return Source.Format(
            orbit.Estimated ? "map.orbitMetaEstimated" : "map.orbitMeta",
            ("source", orbit.Source), ("time", FlightFormat.MetShort(orbit.At)));
    }

    /// <summary>
    /// The fix meta at <paramref name="now"/>: <see cref="OrbitMeta(OrbitFix)"/>, or <c>STALE 34m · ground tracking · MET 06:58</c>
    /// once the fix is older than its stale time (X8).
    /// </summary>
    public string OrbitMeta(OrbitFix orbit, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(orbit);
        if (!orbit.IsStale(now)) return OrbitMeta(orbit);
        return Source.Format("map.orbitMetaStale",
            ("age", FlightFormat.Age(orbit.AgeS(now))), ("source", orbit.Source), ("time", FlightFormat.MetShort(orbit.At)));
    }

    /// <summary>The orbit chip's badge: <c>STALE 34m</c>, <c>EST</c>, or empty for a fresh fix.</summary>
    public string OrbitBadge(OrbitFix orbit, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(orbit);
        return orbit.StateAt(now) switch
        {
            OrbitFixState.Stale => Source.Format("map.badgeStale", ("age", FlightFormat.Age(orbit.AgeS(now)))),
            OrbitFixState.Estimated => Source.Get("map.badgeEstimated"),
            _ => string.Empty,
        };
    }

    /// <summary><c>T 90:19</c>.</summary>
    public string Period(double seconds) => Source.Format("map.period", ("time", FlightFormat.Period(seconds)));

    /// <summary><c>Corridor −2.0° to −0.8°</c>.</summary>
    public string CorridorBand(double steepestDeg, double shallowestDeg) =>
        Source.Format("map.corridorBand", ("steep", FlightFormat.AngleDeg(steepestDeg)), ("shallow", FlightFormat.AngleDeg(shallowestDeg)));

    /// <summary><c>entry −1.3°</c>: the path's flight-path angle at the entry interface.</summary>
    /// <summary><c>IN ZONE</c> or <c>NEAR EDGE</c> (<c>map.zoneInZone</c>, <c>map.zoneNearEdge</c>); <c>OUTSIDE ZONES</c>; empty without a verdict.</summary>
    public string ZoneVerdictWord(LandingZoneVerdict verdict) => verdict switch
    {
        LandingZoneVerdict.InZone => Source.Get("map.zoneInZone"),
        LandingZoneVerdict.NearEdge => Source.Get("map.zoneNearEdge"),
        LandingZoneVerdict.OutsideZones => OutsideZones,
        _ => string.Empty,
    };

    /// <summary><c>Next fix in 9 min</c> (<c>map.nextFix</c> with <see cref="Relative(MissionTime, MissionTime)"/>).</summary>
    public string NextFix(MissionTime now, MissionTime fixAt) => Source.Format("map.nextFix", ("time", Relative(now, fixAt)));

    public string CorridorEntry(double flightPathDeg) => Source.Format("map.corridorEntry", ("angle", FlightFormat.AngleDeg(flightPathDeg)));

    public string Get(string key) => Source.Get(key);

    public string Format(string key, params (string Name, string Value)[] values) => Source.Format(key, values);
}
