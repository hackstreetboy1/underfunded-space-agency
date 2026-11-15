using Usa.Simulation.Crew;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Scenario;
using Usa.Simulation.Vehicle;

namespace Usa.Simulation.Session;

/// <summary>
/// Raises externally sourced mission events (contract section 6) from area events: crew tasks named in a <c>task:</c> source,
/// staging (each section's <c>jettison</c> event), the first retro ignition, parachute deployment, the crewed vehicle's splashdown and, after the
/// landing zone's recovery wait, <c>recovered</c>. Events the mission evaluates itself (those with a <c>when</c>) are never
/// raised here, and ids the mission does not declare are ignored.
/// </summary>
internal sealed class MissionEventBridge : ISimSystem, IHashContributor
{
    private const long NotScheduled = -1;

    private readonly MissionDirector _director;
    private readonly HashSet<string> _raisable = new(StringComparer.Ordinal);
    private readonly Dictionary<TaskId, List<string>> _byTask = [];
    private readonly Dictionary<ComponentId, string> _chuteEvents = [];
    private readonly HashSet<ComponentId> _retroRockets = [];
    private readonly string _crewedVehicleId;
    private long _recoveryDueMs = NotScheduled;

    public MissionEventBridge(MissionDirector director, VehicleSet vehicles, string crewedVehicleId, EventBus events)
    {
        _director = director;
        _crewedVehicleId = crewedVehicleId;
        foreach (var spec in director.Mission.Events)
        {
            if (spec.When is not null) continue;
            _raisable.Add(spec.Id);
            if (spec.Source is { } source && source.StartsWith(MissionEventIds.TaskSourcePrefix, StringComparison.Ordinal))
            {
                var task = new TaskId(source[MissionEventIds.TaskSourcePrefix.Length..].Trim());
                if (!_byTask.TryGetValue(task, out var list)) _byTask[task] = list = [];
                list.Add(spec.Id);
            }
        }

        foreach (var state in vehicles.States)
        {
            var layout = state.Layout;
            foreach (var rocket in layout.RetroRockets) _retroRockets.Add(layout.IdOf(rocket));
            var chutes = layout.Parachutes.ToArray();
            Array.Sort(chutes, (a, b) => layout[b].Parachute!.NominalDeployAltitudeKm.CompareTo(layout[a].Parachute!.NominalDeployAltitudeKm));
            if (chutes.Length >= 1) _chuteEvents[layout.IdOf(chutes[0])] = MissionEventIds.Drogue;
            if (chutes.Length >= 2) _chuteEvents[layout.IdOf(chutes[^1])] = MissionEventIds.Main;
        }

        events.Subscribe(OnEvent);
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public MissionTime? RecoveryDue => _recoveryDueMs == NotScheduled ? null : new MissionTime(_recoveryDueMs);

    public bool Raise(string eventId, MissionTime now) => _raisable.Contains(eventId) && _director.RaiseEvent(eventId, now);

    public void Step(in StepContext context)
    {
        if (_recoveryDueMs == NotScheduled || context.Now.Milliseconds < _recoveryDueMs) return;
        _recoveryDueMs = NotScheduled;
        Raise(MissionEventIds.Recovered, context.Now);
    }

    public void WriteHash(StateHashWriter writer) => writer.Write(_recoveryDueMs);

    private void OnEvent(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case CrewTaskCompleted completed when _byTask.TryGetValue(completed.Task, out var ids):
                foreach (var id in ids) Raise(id, completed.Time);
                break;
            case SectionJettisoned jettisoned:
                Raise(jettisoned.EventId, jettisoned.Time);
                break;
            case ComponentStateChanged { Field: ComponentFields.Fired } fired when _retroRockets.Contains(fired.Component) && fired.Value.AsBool():
                Raise(MissionEventIds.Retrofire, fired.Time);
                break;
            case ComponentStateChanged { Field: ComponentFields.State } chute when _chuteEvents.TryGetValue(chute.Component, out var chuteEvent) &&
                                                                                    chute.Value.AsText() == ComponentFields.ParachuteName(ParachuteStatus.Deployed):
                Raise(chuteEvent, chute.Time);
                break;
            case Splashdown splashdown when splashdown.VehicleId == _crewedVehicleId:
                Raise(MissionEventIds.Splashdown, splashdown.Time);
                _recoveryDueMs = splashdown.Time.Milliseconds + MissionTime.FromSeconds(splashdown.RecoveryWaitHours * 3600.0).Milliseconds;
                break;
        }
    }
}
