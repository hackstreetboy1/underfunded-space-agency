using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// Finds when a coasting vehicle's longitude (inertial angle minus the body's rotation) next falls inside each
/// recovery zone, scanning the conic and bisecting the entry to 1 s. Results are cached per conic until the pass
/// starts, so the per-frame cost is a dictionary lookup.
/// </summary>
public sealed class ZonePassPredictor
{
    private const long RefineMs = 1000;

    private readonly RecoveryZones _zones;
    private readonly double _scanStepS;
    private readonly double _horizonOrbits;
    private readonly Dictionary<string, CachedPass> _cache = new(StringComparer.Ordinal);
    private Conic _cachedConic;
    private bool _hasConic;

    public ZonePassPredictor(RecoveryZones zones, double scanStepS = 20, double horizonOrbits = 2)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scanStepS);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizonOrbits);
        _zones = zones;
        _scanStepS = scanStepS;
        _horizonOrbits = horizonOrbits;
    }

    /// <summary>Scans performed (cache misses).</summary>
    public int ScanCount { get; private set; }

    /// <summary>
    /// The start of the next pass over <paramref name="zone"/> at or after <paramref name="now"/> (now itself when the
    /// vehicle is over the zone), or null if none happens before <paramref name="until"/> or the horizon.
    /// </summary>
    public MissionTime? NextPass(in Conic conic, RecoveryZone zone, MissionTime now, MissionTime? until = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (!_hasConic || _cachedConic != conic)
        {
            _cache.Clear();
            _cachedConic = conic;
            _hasConic = true;
        }

        if (_cache.TryGetValue(zone.Id, out var cached) && cached.IsValidAt(now, until)) return cached.Pass;
        var horizon = HorizonFrom(conic, now, until);
        var pass = Scan(conic, zone, now, horizon);
        ScanCount++;
        _cache[zone.Id] = new CachedPass(now, until, pass);
        return pass;
    }

    public bool IsOver(in Conic conic, RecoveryZone zone, MissionTime time)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return zone.Contains(LongitudeAt(conic, time));
    }

    private MissionTime HorizonFrom(in Conic conic, MissionTime now, MissionTime? until)
    {
        var periodS = conic.IsHyperbolic ? 3 * 3600 : conic.PeriodS;
        var horizon = now + (long)(periodS * _horizonOrbits * 1000);
        return until is { } limit && limit < horizon ? limit : horizon;
    }

    private MissionTime? Scan(in Conic conic, RecoveryZone zone, MissionTime now, MissionTime horizon)
    {
        if (IsOver(conic, zone, now)) return now;
        var stepMs = (long)(_scanStepS * 1000);
        var previous = now;
        while (previous < horizon)
        {
            var next = new MissionTime(Math.Min(previous.Milliseconds + stepMs, horizon.Milliseconds));
            if (IsOver(conic, zone, next)) return Refine(conic, zone, previous, next);
            previous = next;
        }
        return null;
    }

    private MissionTime Refine(in Conic conic, RecoveryZone zone, MissionTime outside, MissionTime inside)
    {
        while (inside.Milliseconds - outside.Milliseconds > RefineMs)
        {
            var mid = new MissionTime(outside.Milliseconds + (inside.Milliseconds - outside.Milliseconds) / 2);
            if (IsOver(conic, zone, mid)) inside = mid;
            else outside = mid;
        }
        return inside;
    }

    private double LongitudeAt(in Conic conic, MissionTime time) =>
        _zones.LandingLongitudeRad(conic.StateAt(time).PositionM.Angle, time);

    /// <summary>A pass found (or not) by a scan starting at <see cref="From"/>.</summary>
    private readonly record struct CachedPass(MissionTime From, MissionTime? Until, MissionTime? Pass)
    {
        /// <summary>A scan that found nothing is repeated after this long, as the horizon moves on.</summary>
        private const long NoPassRetryMs = 60_000;

        public bool IsValidAt(MissionTime now, MissionTime? until)
        {
            if (now < From || until != Until) return false;
            return Pass is { } pass ? now <= pass : now.Milliseconds - From.Milliseconds <= NoPassRetryMs;
        }
    }
}
