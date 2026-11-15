using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Vehicle;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Session;

/// <summary>
/// Records the run facts scoring needs that no area keeps (every flight step): peak g below the entry interface, every chute
/// deployment result, seconds with an unacknowledged master alarm, critical components ever failed, and docking damage.
/// Critical components are the crewed vehicle's survival and return loads outside jettisonable sections, thruster groups, buses, fuel cells, batteries
/// and regulators.
/// </summary>
internal sealed class RunTracker : ISimSystem, IHashContributor
{
    private readonly FlightSystem _flight;
    private readonly AlarmMonitor _alarms;
    private readonly VehicleState _crewed;
    private readonly int[] _critical;
    private readonly bool[] _everFailed;
    private readonly List<ChuteDeployResult> _chuteResults = [];

    public RunTracker(FlightSystem flight, AlarmMonitor alarms, VehicleState crewed, EventBus events)
    {
        _flight = flight;
        _alarms = alarms;
        _crewed = crewed;
        var layout = crewed.Layout;
        var critical = new List<int>();
        for (var i = 0; i < layout.Count; i++)
        {
            var c = layout[i];
            // Sections the crew jettisons before entry (equipment, retro) are not needed for the return.
            if (layout.Definition.Sections[layout.SectionOf(i)].Jettison is not null) continue;
            var load = c.Load?.Priority is LoadPriority.Survival or LoadPriority.Return;
            if (load || c.Family is ComponentFamily.ThrusterGroup or ComponentFamily.Bus or ComponentFamily.FuelCell
                    or ComponentFamily.Battery or ComponentFamily.Regulator)
                critical.Add(i);
        }
        _critical = [.. critical];
        _everFailed = new bool[_critical.Length];
        events.Subscribe(OnEvent);
    }

    public int PeriodTicks => 2;

    public double EntryPeakG { get; private set; }

    public double MasterAlarmSeconds { get; private set; }

    public int DockingDamageCount { get; private set; }

    public IReadOnlyList<ChuteDeployResult> ChuteResults => _chuteResults;

    public int CriticalTotal => _critical.Length;

    public int CriticalRemaining
    {
        get
        {
            var remaining = 0;
            foreach (var index in _critical)
            {
                if (!IsLost(index)) remaining++;
            }
            return remaining;
        }
    }

    public bool AnyCriticalEverFailed => Array.IndexOf(_everFailed, true) >= 0;

    public void Step(in StepContext context)
    {
        var kinematics = _flight.Kinematics;
        if (kinematics.Phase == FlightPhase.Atmospheric && kinematics.GLoad > EntryPeakG) EntryPeakG = kinematics.GLoad;
        if (_alarms.MasterUnacknowledged) MasterAlarmSeconds += context.DeltaSeconds;
        for (var i = 0; i < _critical.Length; i++)
        {
            if (IsLost(_critical[i])) _everFailed[i] = true;
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        writer.Write(EntryPeakG);
        writer.Write(MasterAlarmSeconds);
        writer.Write((long)DockingDamageCount);
        foreach (var result in _chuteResults) writer.Write((long)result);
        foreach (var failed in _everFailed) writer.Write(failed);
    }

    /// <summary>
    /// Failed, or (an attached load that is switched on) with a dead feed: a burned harness loses the RCS ring drivers until a
    /// repair jumper powers them again. Components of a jettisoned section no longer count.
    /// </summary>
    private bool IsLost(int index)
    {
        if (!_crewed.IsAttached(index)) return false;
        ref readonly var state = ref _crewed[index];
        return state.Condition == ComponentCondition.Failed || (_crewed.Layout[index].Load is not null && state.On && !state.Energized);
    }

    private void OnEvent(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case ChuteDeployed chute when chute.VehicleId == _crewed.VehicleId:
                _chuteResults.Add(chute.Result);
                break;
            case DockingDamaged:
                DockingDamageCount++;
                break;
        }
    }
}
