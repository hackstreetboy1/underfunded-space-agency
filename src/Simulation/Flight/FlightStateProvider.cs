using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Flight;

/// <summary>
/// Serves <c>flight.&lt;field&gt;</c> for the mission's crewed vehicle (or the docked stack):
/// <c>docked</c>, <c>phase</c> ('coast'|'powered'|'atmospheric'|'landed'), <c>landed</c>, <c>body</c>,
/// <c>altitude_km</c>, <c>periapsis_km</c>, <c>apoapsis_km</c> (infinite when hyperbolic), <c>speed_mps</c> (inertial),
/// <c>airspeed_mps</c>, <c>flight_path_angle_deg</c>, <c>g_load</c>, <c>heating_proxy</c>,
/// <c>drogue_deployed</c>, <c>main_deployed</c>, <c>range_m</c> (to the target, 0 docked or without one),
/// <c>zone</c> ('' before landing, the zone id, or 'none'), <c>recovery_wait_h</c> and <c>landing_longitude_deg</c> (0 before landing).
/// With a <see cref="SunModel"/> it also serves <c>sun_deg</c>, <c>sunlit</c> and <c>sunlit_fraction</c>, and per flown vehicle
/// <c>flight.&lt;v&gt;.sunlit</c> and <c>flight.&lt;v&gt;.sunlit_fraction</c> (see <see cref="SunPaths"/>).
/// </summary>
/// <param name="sun">The mission's Sun; null (mission 1) leaves the Sun paths unknown.</param>
public sealed class FlightStateProvider(FlightSystem flight, SunModel? sun = null) : IStateProvider
{
    public const string NoZone = "none";

    private const string SunDeg = "sun_deg";
    private const string Sunlit = "sunlit";
    private const string SunlitFraction = "sunlit_fraction";

    private static readonly string[] Fields =
    [
        "airspeed_mps", "altitude_km", "apoapsis_km", "body", "docked", "drogue_deployed", "flight_path_angle_deg",
        "g_load", "heating_proxy", "landed", "landing_longitude_deg", "main_deployed", "periapsis_km", "phase",
        "range_m", "recovery_wait_h", "speed_mps", "zone",
    ];

    private readonly FlightSystem _flight = flight ?? throw new ArgumentNullException(nameof(flight));

    public string Prefix => StatePaths.Flight;

    /// <summary>Every path this provider serves without a Sun, sorted.</summary>
    public static IEnumerable<string> KnownPaths => Fields.Select(name => StatePaths.Flight + name);

    /// <summary>The paths a <see cref="SunModel"/> adds for the flown vehicles <paramref name="vehicleIds"/>, sorted.</summary>
    public static IEnumerable<string> SunPaths(IEnumerable<string> vehicleIds)
    {
        ArgumentNullException.ThrowIfNull(vehicleIds);
        string[] shared = [SunDeg, Sunlit, SunlitFraction];
        return shared.Select(name => StatePaths.Flight + name)
            .Concat(vehicleIds.SelectMany(v => new[] { $"{StatePaths.Flight}{v}.{Sunlit}", $"{StatePaths.Flight}{v}.{SunlitFraction}" }))
            .Order(StringComparer.Ordinal);
    }

    public bool IsKnownPath(string path) =>
        path.StartsWith(StatePaths.Flight, StringComparison.Ordinal) &&
        (Array.IndexOf(Fields, path[StatePaths.Flight.Length..]) >= 0 || (sun is not null && TryGetSun(sun, path.AsSpan(StatePaths.Flight.Length), out _)));

    public bool TryGet(string path, out StateValue value)
    {
        if (!path.StartsWith(StatePaths.Flight, StringComparison.Ordinal))
        {
            value = StateValue.None;
            return false;
        }

        var field = path.AsSpan(StatePaths.Flight.Length);
        if (sun is not null && TryGetSun(sun, field, out value)) return true;
        var kinematics = _flight.Kinematics;
        var radius = _flight.Crewed.Body.RadiusM;
        var landing = _flight.Landing;
        value = field switch
        {
            "docked" => _flight.Docked,
            "phase" => kinematics.Phase.ToStateText(),
            "landed" => kinematics.Phase == FlightPhase.Landed,
            "body" => kinematics.BodyId,
            "altitude_km" => kinematics.AltitudeM / 1000,
            "periapsis_km" => (_flight.Conic.PeriapsisM - radius) / 1000,
            "apoapsis_km" => (_flight.Conic.ApoapsisM - radius) / 1000,
            "speed_mps" => kinematics.Phase == FlightPhase.Landed ? 0 : kinematics.SpeedMps,
            "airspeed_mps" => kinematics.AirspeedMps,
            "flight_path_angle_deg" => kinematics.Phase == FlightPhase.Landed ? 0 : kinematics.FlightPathAngleRad * Angles.RadToDeg,
            "g_load" => kinematics.GLoad,
            "heating_proxy" => kinematics.HeatingProxy,
            "drogue_deployed" => _flight.Drogue == ChuteState.Deployed,
            "main_deployed" => _flight.Main == ChuteState.Deployed,
            "range_m" => _flight.RangeM,
            "zone" => landing is { } site ? site.Zone.ZoneId ?? NoZone : "",
            "recovery_wait_h" => landing?.Zone.RecoveryWaitHours ?? 0,
            "landing_longitude_deg" => landing?.Zone.LandingLongitudeDeg ?? 0,
            _ => StateValue.None,
        };
        return value.Kind != StateValueKind.None;
    }

    private bool TryGetSun(SunModel model, ReadOnlySpan<char> field, out StateValue value)
    {
        value = StateValue.None;
        var dot = field.LastIndexOf('.');
        string? vehicle = null;
        if (dot >= 0)
        {
            vehicle = FlownVehicle(field[..dot]);
            if (vehicle is null) return false;
        }
        else if (field.SequenceEqual(SunDeg))
        {
            value = model.SunDeg;
            return true;
        }
        var name = field[(dot + 1)..];
        var isSunlit = name.SequenceEqual(Sunlit);
        if (!isSunlit && !name.SequenceEqual(SunlitFraction)) return false;
        var fraction = vehicle is null ? model.CrewedSunlitFraction() : model.SunlitFraction(vehicle);
        value = isSunlit ? StateValue.Of(fraction >= SunModel.SunlitThreshold) : StateValue.Of(fraction);
        return true;
    }

    private string? FlownVehicle(ReadOnlySpan<char> id) =>
        id.SequenceEqual(_flight.CrewedId) ? _flight.CrewedId
        : _flight.TargetId is { } target && id.SequenceEqual(target) ? target
        : null;
}
