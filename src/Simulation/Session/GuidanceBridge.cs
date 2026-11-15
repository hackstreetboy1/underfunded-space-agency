using Usa.Simulation.Attitude;
using Usa.Simulation.Commands;
using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Vehicle;

namespace Usa.Simulation.Session;

/// <summary>
/// Feeds the guidance director (runs just before it, 1 s): the activity flags (crew slot busy, a burn running, unacknowledged
/// master alarms, the host's "reading a reference" flag) and signals derived from events — refused commands and failed
/// tasks are failed actions, observations are diagnostics, a repair jumper is a repair, a falling rate crossing or a cleared
/// alarm is a stabilization.
/// </summary>
internal sealed class GuidanceBridge : ISimSystem
{
    private readonly GuidanceDirector _guidance;
    private readonly CrewTaskRunner _runner;
    private readonly BurnController _burns;
    private readonly AlarmMonitor _alarms;

    public GuidanceBridge(GuidanceDirector guidance, CrewTaskRunner runner, BurnController burns, AlarmMonitor alarms, EventBus events)
    {
        _guidance = guidance;
        _runner = runner;
        _burns = burns;
        _alarms = alarms;
        events.Subscribe(OnEvent);
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    /// <summary>Set by the host while a report or encyclopedia entry is open. Presentation input: replays do not reproduce it.</summary>
    public bool ReadingReference { get; set; }

    public void Step(in StepContext context) =>
        _guidance.Activity = new GuidanceActivity(
            _runner.Active is not null,
            ReadingReference,
            _burns.AnyBurn,
            _alarms.UnacknowledgedCount(AlarmLevel.Master));

    private void OnEvent(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case CommandRefused or CrewTaskFailed:
                _guidance.Signal(new GuidanceSignal(GuidanceSignalKind.FailedAction));
                break;
            case ObservationRecorded recorded:
                _guidance.Signal(new GuidanceSignal(GuidanceSignalKind.Diagnostic, recorded.Observation.Fact));
                break;
            case ComponentStateChanged { Field: ComponentFields.Jumper }:
                _guidance.Signal(new GuidanceSignal(GuidanceSignalKind.Repair));
                break;
            case RateThresholdCrossed { Rising: false } or AlarmCleared:
                _guidance.Signal(new GuidanceSignal(GuidanceSignalKind.Stabilization));
                break;
        }
    }
}
