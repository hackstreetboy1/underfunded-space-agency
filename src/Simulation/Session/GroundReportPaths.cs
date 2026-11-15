using Usa.Simulation.Attitude;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// What the ground is told, as state for <see cref="FactSourceKind.Report"/> facts (recorded by <see cref="GroundReportRecorder"/>):
/// <list type="table">
/// <item><term>report.&lt;v&gt;.attitude_mode</term><description>R text, per attitude vehicle: <c>'free'</c> (control off),
/// <c>'rate_damp'</c>, <c>'retrograde_hold'</c> (attitude hold within <see cref="RetrogradeToleranceDeg"/> of retrograde) or
/// <c>'custom'</c> (any other hold).</description></item>
/// <item><term>report.&lt;v&gt;.control_set</term><description>R text: the holding system, <c>'none'</c> without one. A set of
/// several thruster groups names the one armed group when only one is (<c>'rcs_a'</c> for <c>rcs</c> with only
/// <c>tandem/rcs/ring_a</c> armed: the text after the group's last <c>_</c>), else the set id.</description></item>
/// <item><term>report.recovery.zone</term><description>R text: the zone id the recovery forces report (<c>'none'</c> outside
/// every zone); no value before splashdown.</description></item>
/// <item><term>report.recovery.splash_longitude_deg</term><description>R number: the reported splash longitude (exact: the
/// forces are on scene); no value before splashdown.</description></item>
/// </list>
/// Commanded modes and arming are what the ground ordered or hears over the loop; nothing here reads hidden truth such as a
/// stuck thruster or a failed driver.
/// </summary>
internal sealed class GroundReportPaths : IStateProvider
{
    public const string AttitudeModeField = "attitude_mode";
    public const string ControlSetField = "control_set";
    public const string RecoveryZone = FactCatalog.ReportPrefix + "recovery.zone";
    public const string RecoverySplashLongitude = FactCatalog.ReportPrefix + "recovery.splash_longitude_deg";

    /// <summary>An attitude hold this close to retrograde reports as <c>'retrograde_hold'</c>; retrograde turns about 4°/min in orbit.</summary>
    public const double RetrogradeToleranceDeg = 10;

    private readonly AttitudeSystem _attitude;
    private readonly FlightSystem _flight;
    private readonly IStateQuery _state;
    private readonly Dictionary<string, (AttitudeVehicle Vehicle, bool ControlSet)> _attitudePaths = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Vehicle, string Set), (string Group, string Word)[]> _groups = [];

    public GroundReportPaths(AttitudeSystem attitude, AttitudeConfigDefinition config, FlightSystem flight, IStateQuery state)
    {
        _attitude = attitude ?? throw new ArgumentNullException(nameof(attitude));
        _flight = flight ?? throw new ArgumentNullException(nameof(flight));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        ArgumentNullException.ThrowIfNull(config);
        foreach (var vehicle in _attitude.Vehicles)
        {
            _attitudePaths[$"{FactCatalog.ReportPrefix}{vehicle.Id}.{AttitudeModeField}"] = (vehicle, false);
            _attitudePaths[$"{FactCatalog.ReportPrefix}{vehicle.Id}.{ControlSetField}"] = (vehicle, true);
        }
        foreach (var vehicle in config.Vehicles)
        {
            foreach (var set in vehicle.ControlSets)
            {
                if (set.Groups is not { Count: > 1 } groups) continue;
                _groups[(vehicle.Id, set.Id)] = [.. groups.Select(g => (g, $"{set.Id}_{g[(g.LastIndexOf('_') + 1)..]}"))];
            }
        }
    }

    public string Prefix => FactCatalog.ReportPrefix;

    /// <summary>Every path served, sorted.</summary>
    public IEnumerable<string> KnownPaths => _attitudePaths.Keys.Append(RecoveryZone).Append(RecoverySplashLongitude).Order(StringComparer.Ordinal);

    public bool IsKnownPath(string path) => _attitudePaths.ContainsKey(path) || path is RecoveryZone or RecoverySplashLongitude;

    public bool TryGet(string path, out StateValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        value = StateValue.None;
        if (_attitudePaths.TryGetValue(path, out var entry))
        {
            value = entry.ControlSet ? ControlSetWord(entry.Vehicle) : ModeWord(entry.Vehicle);
            return true;
        }
        if (path is not (RecoveryZone or RecoverySplashLongitude)) return false;
        if (_flight.Landing is { } landing)
        {
            value = path == RecoveryZone ? StateValue.Of(landing.Zone.ZoneId ?? FlightStateProvider.NoZone) : StateValue.Of(landing.Zone.LandingLongitudeDeg);
        }
        return true;
    }

    private string ModeWord(AttitudeVehicle vehicle) => vehicle.Mode switch
    {
        AttitudeMode.Off => "free",
        AttitudeMode.RateDamp => "rate_damp",
        _ when vehicle.ControlSetId.Length == 0 => "free",
        _ => IsRetrograde(vehicle) ? "retrograde_hold" : "custom",
    };

    private bool IsRetrograde(AttitudeVehicle vehicle)
    {
        var trajectory = vehicle.Id == _flight.TargetId && _flight.Target is { } target ? target.State : _flight.Kinematics.State;
        var retroDeg = BurnFrames.InertialAngleRad(BurnDirection.Retrograde, 0, trajectory) * Angles.RadToDeg;
        return Math.Abs(Angles.Wrap((vehicle.HoldTargetDeg - retroDeg) * Angles.DegToRad)) * Angles.RadToDeg <= RetrogradeToleranceDeg;
    }

    private string ControlSetWord(AttitudeVehicle vehicle)
    {
        var set = vehicle.ControlSetId;
        if (set.Length == 0) return "none";
        if (!_groups.TryGetValue((vehicle.Id, set), out var groups)) return set;
        string? armed = null;
        foreach (var (group, word) in groups)
        {
            if (!_state.TryGet(group + ".armed", out var on) || on.Kind != StateValueKind.Bool || !on.AsBool()) continue;
            if (armed is not null) return set;
            armed = word;
        }
        return armed ?? set;
    }
}
