using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Vehicle.Docking;

/// <summary>
/// The docking interface between a target (Lodestar's cone) and a chaser (Tandem's docking bar), stepped once per second.
/// <para>While latched, structural load = |roll rate| / fullLoadRollRateDps (target port's data). Above
/// alarmRollRateDps the load alarm is active and overstress time accumulates; it resets as soon as the rate drops.
/// Once it reaches damageAfterS the target port becomes degraded with fault <c>overstress</c>; undocking stays possible.</para>
/// <para>Latching changes made through effects on either port's <c>latched</c> field are mirrored to the other port at once.</para>
/// </summary>
public sealed class DockingModel : ISimSystem
{
    private readonly VehicleState _target;
    private readonly VehicleState _chaser;
    private readonly int _targetPort;
    private readonly int _chaserPort;
    private readonly IStateQuery _state;
    private readonly string _rollRatePath;
    private bool _syncing;

    /// <param name="state">Source of <c>attitude.&lt;chaserId&gt;.rate_roll_dps</c>; a missing path reads as zero.</param>
    /// <exception cref="InvalidOperationException">No docking port of the target names a port of the chaser as partner.</exception>
    public DockingModel(VehicleState target, VehicleState chaser, IStateQuery state)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(chaser);
        ArgumentNullException.ThrowIfNull(state);
        _target = target;
        _chaser = chaser;
        _state = state;
        (_targetPort, _chaserPort) = FindPorts(target, chaser);
        _rollRatePath = $"{StatePaths.Attitude}{chaser.VehicleId}.rate_roll_dps";

        IsLatched = target[_targetPort].Latched || chaser[_chaserPort].Latched;
        target.Mutable(_targetPort).Latched = IsLatched;
        chaser.Mutable(_chaserPort).Latched = IsLatched;

        target.Events.Subscribe(OnEvent);
        if (!ReferenceEquals(target.Events, chaser.Events)) chaser.Events.Subscribe(OnEvent);
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public bool IsLatched { get; private set; }

    public ComponentId TargetPort => _target.Layout.IdOf(_targetPort);

    public ComponentId ChaserPort => _chaser.Layout.IdOf(_chaserPort);

    /// <returns>False when either port is detached, failed or isolated.</returns>
    public bool Latch(MissionTime now)
    {
        if (IsLatched) return true;
        if (!_target.IsOperable(_targetPort) || !_chaser.IsOperable(_chaserPort)) return false;
        Apply(true, now);
        return true;
    }

    public void Unlatch(MissionTime now)
    {
        if (IsLatched) Apply(false, now);
    }

    public void Step(in StepContext context)
    {
        ref var port = ref _target.Mutable(_targetPort);
        if (!IsLatched)
        {
            SetLoad(0);
            port.OverstressS = 0;
            SetAlarm(false, context.Now);
            return;
        }

        var spec = _target.Layout[_targetPort].Docking!;
        var rateDps = _state.TryGet(_rollRatePath, out var value) ? Math.Abs(value.AsNumber()) : 0;
        SetLoad(rateDps / spec.FullLoadRollRateDps);

        if (rateDps > spec.AlarmRollRateDps)
        {
            port.OverstressS += context.DeltaSeconds;
            SetAlarm(true, context.Now);
        }
        else
        {
            port.OverstressS = 0;
            SetAlarm(false, context.Now);
        }

        if (port.OverstressS >= spec.DamageAfterS && port.Condition == ComponentCondition.Nominal)
        {
            _target.SetCondition(_targetPort, ComponentCondition.Degraded, context.Now);
            _target.SetFault(_targetPort, ComponentFields.OverstressFault, context.Now);
            _target.Events.Publish(new DockingDamaged(context.Now, TargetPort));
        }
    }

    private void OnEvent(ISimEvent simEvent)
    {
        if (_syncing || simEvent is not ComponentStateChanged { Field: ComponentFields.Latched } change) return;
        var isPort = change.Component == TargetPort || change.Component == ChaserPort;
        if (!isPort || change.Value.AsBool() == IsLatched) return;

        if (change.Value.AsBool() && (!_target.IsOperable(_targetPort) || !_chaser.IsOperable(_chaserPort)))
        {
            Sync(false, change.Time);
            return;
        }
        Apply(change.Value.AsBool(), change.Time);
    }

    private void Apply(bool latched, MissionTime now)
    {
        IsLatched = latched;
        Sync(latched, now);
        if (!latched)
        {
            SetLoad(0);
            _target.Mutable(_targetPort).OverstressS = 0;
            SetAlarm(false, now);
        }
        _target.Events.Publish(new DockingStateChanged(now, TargetPort, ChaserPort, latched));
    }

    private void Sync(bool latched, MissionTime now)
    {
        _syncing = true;
        try
        {
            _target.SetLatched(_targetPort, latched, now);
            _chaser.SetLatched(_chaserPort, latched, now);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SetLoad(double fraction)
    {
        _target.Mutable(_targetPort).LoadFraction = fraction;
        _chaser.Mutable(_chaserPort).LoadFraction = fraction;
    }

    private void SetAlarm(bool active, MissionTime now)
    {
        ref var port = ref _target.Mutable(_targetPort);
        if (port.OverstressAlarm == active) return;
        port.OverstressAlarm = active;
        _target.Events.Publish(new DockingOverstressChanged(now, TargetPort, active, port.LoadFraction));
    }

    private static (int Target, int Chaser) FindPorts(VehicleState target, VehicleState chaser)
    {
        foreach (var port in target.Layout.DockingPorts)
        {
            var partner = target.Layout[port].Docking!.Partner.Value;
            if (chaser.Layout.TryGetIndex(partner, out var chaserPort)) return (port, chaserPort);
        }
        throw new InvalidOperationException($"No docking port of '{target.VehicleId}' mates with '{chaser.VehicleId}'.");
    }
}
