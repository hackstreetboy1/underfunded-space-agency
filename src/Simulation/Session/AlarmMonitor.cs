using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Health;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;
using Usa.Simulation.Vehicle;

namespace Usa.Simulation.Session;

/// <summary>
/// The annunciator. It has no thresholds of its own: every limit comes from data the areas already use.
/// <list type="table">
/// <item><term>rate.&lt;crewed vehicle&gt;</term><description>Warning once the rate impairs crew work (crew.json), Master once it harms health (health.json curve), from <c>attitude.&lt;v&gt;.rate_total_dps</c>.</description></item>
/// <item><term>cabin.ppco2 / ppo2 / pressure / temp</term><description>Warning while the cabin reading harms health (health.json curves). The reading is the
/// cabin sensor that measures the health exposure path (bias included, offline reads nothing), or the true value when no sensor does.</description></item>
/// <item><term>crew.&lt;id&gt;</term><description>Warning while impaired, Master while critical.</description></item>
/// <item><term>power.brownout.&lt;bus&gt;</term><description>Warning while the bus is in brownout.</description></item>
/// <item><term>docking.overstress</term><description>Warning while the docking load alarm is active.</description></item>
/// <item><term>downlink.&lt;fact&gt;</term><description>Caution while a fact's ground telemetry sits in its lowest band (e.g. OAMS propellant LOW), or in its highest band when the lowest is NOMINAL (docking load OVERLOAD).</description></item>
/// <item><term>tank.&lt;id&gt;</term><description>Caution once a tank is empty.</description></item>
/// </list>
/// </summary>
public sealed class AlarmMonitor : ISimSystem, IHashContributor
{
    private readonly IStateQuery _state;
    private readonly HealthConfig _health;
    private readonly CrewPerformance _performance;
    private readonly EventBus _events;
    private readonly string _ratePath;
    private readonly string _rateAlarmId;
    private readonly string _crewedVehicle;
    private readonly CabinReading[] _cabin;
    private const string NominalBand = "NOMINAL";

    private readonly Dictionary<FactId, string> _lowestBand = [];
    private readonly SortedDictionary<string, ActiveAlarm> _active = new(StringComparer.Ordinal);

    // Unacknowledged active alarms per level, kept with _active so the per-tick queries never enumerate it (that allocates).
    private readonly int[] _unacknowledged = new int[Enum.GetValues<AlarmLevel>().Length];
    private MissionTime _now;

    internal AlarmMonitor(
        IStateQuery state,
        HealthConfig health,
        CrewExposurePaths exposurePaths,
        CrewPerformance performance,
        FactCatalog facts,
        VehicleSet vehicles,
        string crewedVehicle,
        EventBus events)
    {
        _state = state;
        _health = health;
        _performance = performance;
        _events = events;
        _crewedVehicle = crewedVehicle;
        _ratePath = $"{StatePaths.Attitude}{crewedVehicle}.rate_total_dps";
        _rateAlarmId = $"rate.{crewedVehicle}";
        var paths = exposurePaths;
        _cabin =
        [
            CabinReading.Create("cabin.ppco2", paths.Ppco2Mmhg, HealthCause.Ppco2, vehicles),
            CabinReading.Create("cabin.ppo2", paths.Ppo2Kpa, HealthCause.LowPpo2, vehicles),
            CabinReading.Create("cabin.pressure", paths.PressureKpa, HealthCause.LowPressure, vehicles),
            CabinReading.Create("cabin.temp", paths.TemperatureC, HealthCause.HighTemperature, vehicles, HealthCause.LowTemperature),
        ];
        foreach (var fact in facts.Facts)
        {
            if (fact.Downlink is not { Bands.Count: > 1 } downlink) continue;
            // Bands ascend by value; a fact whose lowest band is the nominal one (docking load) alarms in its highest band.
            var lowest = downlink.Bands[0].Word;
            _lowestBand[fact.Id] = string.Equals(lowest, NominalBand, StringComparison.OrdinalIgnoreCase) ? downlink.Bands[^1].Word : lowest;
        }
        events.Subscribe(OnEvent);
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    /// <summary>Active alarms in id order.</summary>
    public IReadOnlyCollection<ActiveAlarm> Active => _active.Values;

    public bool MasterUnacknowledged => _unacknowledged[(int)AlarmLevel.Master] > 0;

    public int UnacknowledgedCount(AlarmLevel atLeast)
    {
        var count = 0;
        for (var level = (int)atLeast; level < _unacknowledged.Length; level++) count += _unacknowledged[level];
        return count;
    }

    public void AcknowledgeAll(MissionTime now)
    {
        var count = 0;
        foreach (var id in _active.Keys.ToArray())
        {
            var alarm = _active[id];
            if (alarm.Acknowledged) continue;
            _active[id] = alarm with { Acknowledged = true };
            count++;
        }
        Array.Clear(_unacknowledged);
        _events.Publish(new AlarmsAcknowledged(now, count));
    }

    public void Step(in StepContext context)
    {
        _now = context.Now;
        if (_state.TryGet(_ratePath, out var rate))
        {
            var dps = Math.Abs(rate.AsNumber());
            var level = _health.DamagePerS(HealthCause.AngularRate, dps) > 0 ? AlarmLevel.Master
                : _performance.IsRateImpaired(dps) ? AlarmLevel.Warning
                : (AlarmLevel?)null;
            Set(_rateAlarmId, level, _crewedVehicle, dps);
        }

        foreach (var reading in _cabin)
        {
            if (reading.Read(_state) is not { } value)
            {
                Set(reading.AlarmId, null, reading.Path, null);
                continue;
            }
            var harmful = _health.DamagePerS(reading.Cause, value) > 0 ||
                          (reading.SecondCause is { } second && _health.DamagePerS(second, value) > 0);
            Set(reading.AlarmId, harmful ? AlarmLevel.Warning : null, reading.Path, value);
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((long)_active.Count);
        foreach (var alarm in _active.Values)
        {
            writer.Write(alarm.AlarmId);
            writer.Write((long)alarm.Level);
            writer.Write(alarm.Acknowledged);
            writer.Write(alarm.RaisedAt.Milliseconds);
        }
    }

    private void OnEvent(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case BusBrownoutChanged brownout:
                SetAt(brownout.Time, $"power.brownout.{brownout.Bus}", brownout.InBrownout ? AlarmLevel.Warning : null, brownout.Bus.Value, brownout.VoltageV);
                break;
            case DockingOverstressChanged overstress:
                SetAt(overstress.Time, "docking.overstress", overstress.Active ? AlarmLevel.Warning : null, overstress.Port.Value, overstress.LoadFraction * 100);
                break;
            case CrewHealthBandChanged band:
                var level = band.Current switch
                {
                    HealthBand.Critical => AlarmLevel.Master,
                    HealthBand.Impaired => AlarmLevel.Warning,
                    _ => (AlarmLevel?)null,
                };
                SetAt(band.Time, $"crew.{band.Crew}", level, band.Crew.Value, band.Health);
                break;
            case GroundBandChanged ground when _lowestBand.TryGetValue(ground.Fact, out var lowest):
                SetAt(ground.Time, $"downlink.{ground.Fact}", ground.Current == lowest ? AlarmLevel.Caution : null, ground.Fact.Value, null);
                break;
            case TankDepleted depleted:
                SetAt(depleted.Time, $"tank.{depleted.Tank}", AlarmLevel.Caution, depleted.Tank.Value, 0);
                break;
        }
    }

    private void Set(string id, AlarmLevel? level, string subject, double? value) => SetAt(_now, id, level, subject, value);

    private void SetAt(MissionTime time, string id, AlarmLevel? level, string subject, double? value)
    {
        var exists = _active.TryGetValue(id, out var current);
        if (level is not { } wanted)
        {
            if (!exists) return;
            if (!current!.Acknowledged) _unacknowledged[(int)current.Level]--;
            _active.Remove(id);
            _events.Publish(new AlarmCleared(time, id));
            return;
        }
        if (exists && current!.Level == wanted) return;
        if (exists && !current!.Acknowledged) _unacknowledged[(int)current.Level]--;
        _unacknowledged[(int)wanted]++;
        _active[id] = new ActiveAlarm(id, wanted, subject, value, time, Acknowledged: false);
        _events.Publish(new AlarmRaised(time, id, wanted, subject, value));
    }

    private sealed record CabinReading(string AlarmId, string Path, HealthCause Cause, HealthCause? SecondCause, VehicleState? Vehicle, int Sensor)
    {
        public static CabinReading Create(string alarmId, string path, HealthCause cause, VehicleSet vehicles, HealthCause? second = null)
        {
            for (var v = 0; v < vehicles.Count; v++)
            {
                var layout = vehicles[v].Layout;
                foreach (var sensor in layout.Sensors)
                {
                    foreach (var channel in layout[sensor].Sensor!.Channels)
                    {
                        if (channel.Measures == path) return new CabinReading(alarmId, path, cause, second, vehicles[v], sensor);
                    }
                }
            }
            return new CabinReading(alarmId, path, cause, second, null, -1);
        }

        /// <summary>Sensor truth plus bias (no noise, so the alarm does not chatter); null when offline or not served.</summary>
        public double? Read(IStateQuery state)
        {
            if (Path.Length == 0 || !state.TryGet(Path, out var truth) || truth.Kind != StateValueKind.Number) return null;
            if (Vehicle is null) return truth.AsNumber();
            if (!Vehicle.IsOperable(Sensor) || (Vehicle.Layout[Sensor].Load is not null && !Vehicle[Sensor].Powered)) return null;
            return truth.AsNumber() + Vehicle[Sensor].Bias;
        }
    }
}
