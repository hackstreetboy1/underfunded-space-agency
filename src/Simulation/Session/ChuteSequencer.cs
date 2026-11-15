using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Vehicle;

namespace Usa.Simulation.Session;

/// <summary>
/// The entry checklist's automatic chutes (<c>session.entry_checklist.armed</c>). While armed, when the descending capsule passes
/// a chute's nominal deploy altitude from flight.json, the success effects of that chute's deploy task (<c>session.json</c>
/// <c>entryChecklist</c>) are applied, exactly as if the crew had run it; a chute that is no longer stowed is skipped.
/// Checked every flight step.
/// </summary>
internal sealed class ChuteSequencer : ISimSystem, IHashContributor
{
    private readonly FlightSystem _flight;
    private readonly VehicleEventCoordinator _vehicles;
    private readonly EffectRouter _router;
    private readonly IReadOnlyList<EffectDefinition>?[] _effects = new IReadOnlyList<EffectDefinition>?[2];
    private readonly bool[] _done = new bool[2];

    public ChuteSequencer(
        FlightSystem flight, VehicleEventCoordinator vehicles, EffectRouter router, TaskCatalog tasks, TaskBinder binder, EntryChecklistDefinition? checklist)
    {
        _flight = flight;
        _vehicles = vehicles;
        _router = router;
        _effects[(int)ChuteKind.Drogue] = Bind(tasks, binder, checklist?.DrogueTask);
        _effects[(int)ChuteKind.Main] = Bind(tasks, binder, checklist?.MainTask);
    }

    public int PeriodTicks => 2;

    public bool Armed { get; set; }

    public void Step(in StepContext context)
    {
        if (!Armed) return;
        var kinematics = _flight.Kinematics;
        if (kinematics.Phase != FlightPhase.Atmospheric || kinematics.FlightPathAngleRad >= 0) return;
        Deploy(ChuteKind.Drogue, kinematics.AltitudeM, context.Now);
        Deploy(ChuteKind.Main, kinematics.AltitudeM, context.Now);
    }

    public void WriteHash(StateHashWriter writer)
    {
        writer.Write(Armed);
        writer.Write(_done[0]);
        writer.Write(_done[1]);
    }

    private void Deploy(ChuteKind kind, double altitudeM, MissionTime now)
    {
        var k = (int)kind;
        if (_done[k] || altitudeM > _flight.Mission.Capsule.Chute(kind).NominalDeployAltitudeM) return;
        _done[k] = true;
        if (_vehicles.TryGetChute(kind, out var vehicle, out var chute) && vehicle[chute].Parachute != ParachuteStatus.Stowed) return;
        if (_effects[k] is not { } effects) return;
        _router.ApplyAll(effects, now, skipUnroutable: true);
    }

    private static IReadOnlyList<EffectDefinition>? Bind(TaskCatalog tasks, TaskBinder binder, TaskId? taskId) =>
        taskId is { } id && tasks.TryGet(id, out var task) && binder.Bind(task, TaskArguments.Empty) is { IsSuccess: true } bound
            ? bound.Task.OnSuccess
            : null;
}
