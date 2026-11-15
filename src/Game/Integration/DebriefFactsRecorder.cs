using Usa.Game.Content;
using Usa.Simulation.Commands;
using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;
using Usa.Simulation.Session;

namespace Usa.Game.Integration;

/// <summary>
/// The single producer of <see cref="DebriefFlightFacts"/>: collects what the debrief tells about the flight from simulation
/// events, per-frame roll samples and, at the end, the crew's health state. It records ids and values only; the
/// <see cref="DebriefComposer"/> words them from the mission's content.
/// <list type="bullet">
/// <item>Cause: left to the composer, which finds it in the knowledge through the debrief's
/// <see cref="DebriefDefinition.CauseEvidence"/>; the recorder knows no mission's tasks or facts.</item>
/// <item>Approach: B when a mission rule that forces the return took an objective away (the feasibility change names the rule),
/// A for any other recovered flight, so arming a ring only for the return after the objectives were done stays A.</item>
/// <item>Crew conditions: launch, worst and recovery health per crew member from the crew state (<c>crew.&lt;id&gt;.*</c>);
/// recovery is null for a member who died or a flight that was not recovered. Each death's MET goes to the crew losses.</item>
/// <item>Timeline: every incident (<see cref="DebriefTimelineKeys.Incident"/>) and each player decision, the first execution
/// of a decision-kind action with given arguments (<see cref="DebriefTimelineKeys.Decision"/>), carrying the validated command
/// arguments as canonical text.</item>
/// <item>Knowledge: every observation and every component suspicion, confirmed damage or clearing, oldest first. The composer
/// derives each incident's discovery and what was known at each mark from it.</item>
/// <item>Landing and EVA: the splashdown of the mission's crewed vehicle, and the completion of any task whose data kind is
/// <see cref="TaskKind.Eva"/>.</item>
/// </list>
/// </summary>
public sealed class DebriefFactsRecorder
{
    private readonly FactCatalog? _facts;
    private readonly HashSet<TaskId> _evaTasks = [];
    private readonly HashSet<string> _decided = new(StringComparer.Ordinal);
    private readonly HashSet<string> _returnRules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _objectiveLossReasons = new(StringComparer.Ordinal);
    private readonly Dictionary<CrewId, MissionTime> _dead = [];
    private readonly List<DebriefTimelineEvent> _timeline = [];
    private readonly List<DebriefKnowledgeItem> _knowledge = [];
    private string? _crewedVehicleId;
    private double _peakRollDps;
    private MissionTime? _docked;
    private MissionTime? _undocked;
    private bool _evaCompleted;
    private Splashdown? _splashdown;

    /// <summary>Records against the session's mission data: its fact catalog, crewed vehicle and EVA tasks.</summary>
    public DebriefFactsRecorder(MissionData mission)
        : this((mission ?? throw new ArgumentNullException(nameof(mission))).Facts)
    {
        _crewedVehicleId = mission.CrewedVehicleId;
        foreach (var task in mission.Tasks.Tasks)
        {
            if (task.Kind == TaskKind.Eva) _evaTasks.Add(task.Id);
        }
    }

    /// <summary>
    /// Records without the mission data. The crewed vehicle is then learnt from the first docking (or, failing that, is the
    /// first vehicle to splash down) and no task counts as an EVA; prefer <see cref="DebriefFactsRecorder(MissionData)"/>.
    /// </summary>
    /// <param name="facts">
    /// The fact catalog: observations whose method only yields an estimate are left out of the knowledge, since the debrief
    /// reports what was measured or reported. Without it every observation is kept.
    /// </param>
    public DebriefFactsRecorder(FactCatalog? facts = null) => _facts = facts;

    public void SampleRoll(double rollDps)
    {
        if (double.IsFinite(rollDps)) _peakRollDps = Math.Max(_peakRollDps, Math.Abs(rollDps));
    }

    public void Observe(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case DockingChanged { Docked: true } docked:
                _docked ??= docked.Time;
                if (docked.CrewedId.Length > 0) _crewedVehicleId ??= docked.CrewedId;
                break;
            case DockingChanged { Docked: false } undocked:
                _undocked = undocked.Time;
                break;
            case MissionRuleActivated { RequireReturn: not null } rule:
                _returnRules.Add(rule.RuleId);
                break;
            case ObjectiveFeasibilityChanged { To: ObjectiveState.Impossible or ObjectiveState.UnacceptableRisk } lost:
                _objectiveLossReasons.Add(lost.Reason);
                break;
            case CommandExecuted executed:
                OnCommandExecuted(executed);
                break;
            case CrewTaskCompleted completed:
                OnTaskCompleted(completed);
                break;
            case Splashdown splashdown when _crewedVehicleId is null || splashdown.VehicleId == _crewedVehicleId:
                _crewedVehicleId ??= splashdown.VehicleId;
                _splashdown = splashdown;
                break;
            case CrewMemberDied died:
                _dead.TryAdd(died.Crew, died.Time);
                break;
            case IncidentTriggered incident:
                _timeline.Add(new DebriefTimelineEvent(Seconds(incident.Time), DebriefTimelineKind.Incident, DebriefTimelineKeys.Incident(incident.IncidentId)));
                break;
            case ObservationRecorded recorded:
                OnObservation(recorded.Observation);
                break;
            case ComponentKnowledgeChanged changed:
                OnComponentKnowledge(changed);
                break;
        }
    }

    /// <param name="state">Session state at the end, for the crew conditions; null leaves them out.</param>
    /// <param name="crew">The mission's crew, in display order.</param>
    public DebriefFlightFacts Build(bool recovered, IStateQuery? state = null, IReadOnlyList<CrewId>? crew = null) => new(
        PeakRollDps(state),
        DockedMetS: Seconds(_docked),
        UndockedMetS: Seconds(_undocked),
        Approach: recovered ? (_returnRules.Overlaps(_objectiveLossReasons) ? RecoveryApproach.B : RecoveryApproach.A) : null,
        EvaCompleted: _evaCompleted,
        SplashdownMetS: Seconds(_splashdown?.Time),
        RecoveryWaitHours: _splashdown?.RecoveryWaitHours,
        CrewConditions: state is not null && crew is { Count: > 0 } ? CrewConditions(state, crew, recovered) : null,
        Timeline: _timeline.Count > 0 ? [.. _timeline] : null,
        Knowledge: _knowledge.Count > 0 ? [.. _knowledge] : null,
        LandingZoneId: _splashdown?.ZoneId,
        CrewLosses: _dead.Count > 0 ? [.. _dead.OrderBy(d => d.Value.Milliseconds).ThenBy(d => d.Key).Select(d => new DebriefCrewLoss(d.Key, Seconds(d.Value)))] : null);

    private void OnCommandExecuted(CommandExecuted executed)
    {
        if (!DebriefTimelineKeys.DecisionKinds.Contains(executed.Kind)) return;
        var arguments = Arguments(executed.Arguments);
        if (!_decided.Add(DecisionIdentity(executed.Action, arguments))) return;
        _timeline.Add(new DebriefTimelineEvent(Seconds(executed.Time), DebriefTimelineKind.Decision, DebriefTimelineKeys.Decision(executed.Action), arguments));
    }

    private void OnTaskCompleted(CrewTaskCompleted completed)
    {
        if (_evaTasks.Contains(completed.Task)) _evaCompleted = true;
    }

    /// <summary>
    /// Single switch point for review R7: the per-frame samples depend on frame rate and restart on resume, so the session's
    /// deterministic peak (<c>attitude.&lt;crewed&gt;.peak_roll_dps</c>) wins as soon as the state serves it.
    /// </summary>
    private double PeakRollDps(IStateQuery? state) =>
        state is not null && _crewedVehicleId is { } crewed && Number(state, $"attitude.{crewed}.peak_roll_dps") is { } peak
            ? Math.Abs(peak)
            : _peakRollDps;

    private void OnObservation(Observation observation)
    {
        if (_facts is not null && _facts.Capabilities(observation.Method).Yield == ObservationYield.Estimate) return;
        _knowledge.Add(new DebriefKnowledgeItem(
            Seconds(observation.Time), DebriefKnowledgeKind.Observation, observation.Fact.Value,
            observation.Value, observation.Qualitative, observation.Method, observation.Observer));
    }

    private void OnComponentKnowledge(ComponentKnowledgeChanged changed)
    {
        var kind = changed.Current.State switch
        {
            ComponentKnowledgeState.Suspected => DebriefKnowledgeKind.ComponentSuspected,
            ComponentKnowledgeState.ConfirmedDamaged => DebriefKnowledgeKind.ComponentDamaged,
            _ when changed.Previous is ComponentKnowledgeState.Suspected or ComponentKnowledgeState.ConfirmedDamaged => DebriefKnowledgeKind.ComponentCleared,
            _ => (DebriefKnowledgeKind?)null,
        };
        if (kind is not { } recorded) return;
        var variant = recorded == DebriefKnowledgeKind.ComponentDamaged ? changed.Current.Variant : null;
        _knowledge.Add(new DebriefKnowledgeItem(Seconds(changed.Time), recorded, changed.Component.Value, Text: variant));
    }

    /// <returns>Each argument's canonical text by parameter name; null for an action without arguments.</returns>
    private static Dictionary<string, string>? Arguments(IReadOnlyDictionary<string, CommandArg> arguments)
    {
        if (arguments.Count == 0) return null;
        var values = new Dictionary<string, string>(arguments.Count, StringComparer.Ordinal);
        foreach (var (name, value) in arguments) values[name] = value.ToCanonicalString();
        return values;
    }

    /// <summary>The same action with other arguments (another breaker, the other ring) is another decision.</summary>
    private static string DecisionIdentity(ActionId action, IReadOnlyDictionary<string, string>? arguments)
    {
        if (arguments is null) return action.Value;
        return action.Value + "(" + string.Join(",", arguments.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + "=" + a.Value)) + ")";
    }

    private DebriefCrewCondition[] CrewConditions(IStateQuery state, IReadOnlyList<CrewId> crew, bool recovered)
    {
        var conditions = new DebriefCrewCondition[crew.Count];
        for (var i = 0; i < crew.Count; i++)
        {
            var id = crew[i];
            var prefix = $"crew.{id.Value}.";
            var health = Number(state, prefix + "health");
            var alive = state.TryGet(prefix + "alive", out var aliveValue) ? aliveValue.AsBool() : !_dead.ContainsKey(id);
            var cameHome = recovered && alive && !_dead.ContainsKey(id);
            conditions[i] = new DebriefCrewCondition(
                id,
                LaunchHealth: Number(state, prefix + "launch_health") ?? health ?? 0,
                WorstHealth: Number(state, prefix + "worst_health") ?? health ?? 0,
                RecoveryHealth: cameHome ? health : null);
        }

        return conditions;
    }

    private static double? Number(IStateQuery state, string path) =>
        state.TryGet(path, out var value) && value.AsNumber() is var number && double.IsFinite(number) ? number : null;

    private static double Seconds(MissionTime time) => time.Milliseconds / 1000.0;

    private static double? Seconds(MissionTime? time) => time is { } t ? Seconds(t) : null;
}
