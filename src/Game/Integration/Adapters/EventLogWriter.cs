using System.Globalization;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew;
using Usa.Simulation.Flight;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Scenario;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle;
using SimComponentState = Usa.Simulation.Knowledge.ComponentKnowledgeState;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Event-log rows (UI.md 3.8) from simulation events, worded through <see cref="AdapterText"/>. Only what the ground can see
/// is logged: alarms, commands, crew task lifecycle and reports, knowledge changes, phase changes, completed objectives,
/// and staging the crew performed. Truth-only events (incident triggers, component state changes, tank depletion,
/// brownouts) are never logged, and neither is objective feasibility: its rules read truth, so it belongs to the debrief.
/// </summary>
public sealed class EventLogWriter
{
    private readonly MissionData _data;
    private readonly CrewNames _crew;
    private readonly AlarmText _alarms;
    private readonly AdapterText _text;
    private readonly Dictionary<string, string> _componentNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sectionNames = new(StringComparer.Ordinal);
    private readonly List<LogEntry> _pending = [];
    private long _sequence;

    public EventLogWriter(MissionData data, CrewNames crew, AlarmText alarms, AdapterText? text = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(alarms);
        _data = data;
        _crew = crew;
        _alarms = alarms;
        _text = text ?? AdapterText.Default;
        foreach (var vehicle in data.Vehicles)
        {
            foreach (var component in vehicle.Components) _componentNames[component.Id.Value] = component.Name;
            foreach (var section in vehicle.Sections) _sectionNames.TryAdd(section.Id, section.Name);
        }
    }

    private string Ground => _text.Get("console.logSourceGround");

    private string Annunciator => _text.Get("console.logSourceAnnunciator");

    private string Mission => _text.Get("console.logSourceMission");

    /// <summary>Rows produced since the last call, in event order; the buffer is cleared.</summary>
    public IReadOnlyList<LogEntry> TakePending()
    {
        if (_pending.Count == 0) return [];
        var rows = _pending.ToArray();
        _pending.Clear();
        return rows;
    }

    public void Observe(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case AlarmRaised raised:
            {
                _alarms.Observe(raised);
                var (text, _, _) = _alarms.Describe(raised.AlarmId, raised.Subject);
                Add(raised.Time, LogCategory.Alarm, text, Annunciator, AdapterMaps.Priority(raised.Level));
                break;
            }
            case AlarmCleared cleared:
                Add(cleared.Time, LogCategory.SystemChange, Format("logRowAlarmCleared", ("alarm", _alarms.DescribeCleared(cleared.AlarmId).Text)), Annunciator);
                break;
            case AlarmsAcknowledged acknowledged:
                Add(acknowledged.Time, LogCategory.Command,
                    Format("logRowAlarmsAcknowledged", ("count", acknowledged.Count.ToString(CultureInfo.InvariantCulture))), Ground);
                break;
            case CommandExecuted executed when executed.Kind is ActionKind.Remote or ActionKind.Meta:
                Add(executed.Time, LogCategory.Command, Format("logRowCommand", ("action", ActionTitle(executed.Action))), Ground);
                break;
            case CommandExecuted executed when executed.Kind == ActionKind.GlanceRead:
                Add(executed.Time, LogCategory.Crew, Format("logRowReadRequested", ("action", ActionTitle(executed.Action))), Crew(executed.Assignee));
                break;
            case CommandRefused refused:
                // The reason is a code; the comms transcript words it for the player.
                Add(refused.Time, LogCategory.Command, Format("logRowRefused", ("action", ActionTitle(refused.Action))), Ground);
                break;
            case CrewTaskStarted started:
                Add(started.Time, LogCategory.Crew, Format("logRowTaskStarted", ("task", TaskName(started.Task))), _crew.ShortName(started.Assignee));
                break;
            case CrewTaskCompleted completed:
                Add(completed.Time, LogCategory.Crew, Format("logRowTaskCompleted", ("task", TaskName(completed.Task))), _crew.ShortName(completed.Assignee));
                break;
            case CrewTaskFailed failed:
                Add(failed.Time, LogCategory.Crew, Format("logRowTaskFailed", ("task", TaskName(failed.Task))), _crew.ShortName(failed.Assignee));
                break;
            case CrewTaskStopped stopped:
                Add(stopped.Time, LogCategory.Crew, Format("logRowTaskStopped", ("task", TaskName(stopped.Task))), _crew.ShortName(stopped.Assignee));
                break;
            case CrewHealthBandChanged band:
                Add(band.Time, LogCategory.Crew,
                    Format("logRowCrewBand", ("crew", _crew.FullName(band.Crew)), ("band", AdapterMaps.BandWord(band.Current))), _crew.ShortName(band.Crew));
                break;
            case CrewMemberDied died:
                Add(died.Time, LogCategory.Crew, Format("logRowCrewLost", ("crew", _crew.FullName(died.Crew))), Mission);
                break;
            case KnowledgeChanged { Current: KnowledgeState.Conflicting } conflict:
                Add(conflict.Time, LogCategory.Info, Format("logRowConflict", ("fact", FactLabel(conflict.Fact))), Ground);
                break;
            case ComponentKnowledgeChanged component when component.Current.State != component.Previous &&
                                                          component.Current.State is SimComponentState.ConfirmedNominal or SimComponentState.ConfirmedDamaged:
                Add(component.Time, LogCategory.SystemChange, ComponentLine(component.Current), Ground);
                break;
            case GroundBandChanged band:
                Add(band.Time, LogCategory.Info, Format("logRowTelemetry", ("fact", FactLabel(band.Fact)), ("band", band.Current)),
                    _text.Get("console.logSourceTelemetry"));
                break;
            case PhaseChanged phase:
                Add(phase.Time, LogCategory.Decision, Format("logRowPhase", ("phase", PhaseWord(phase.To))), Mission);
                break;
            case ObjectiveCompleted objective:
                Add(objective.Time, LogCategory.Decision, Format("logRowObjectiveComplete", ("objective", ObjectiveTitle(objective.ObjectiveId))), Mission);
                break;
            case DockingChanged docking:
                Add(docking.Time, LogCategory.SystemChange,
                    Format(docking.Docked ? "logRowDocked" : "logRowUndocked", ("vehicle", VehicleName(docking.TargetId))), Mission);
                break;
            case SectionJettisoned jettisoned:
                Add(jettisoned.Time, LogCategory.SystemChange, Format("logRowJettisoned", ("section", SectionName(jettisoned.SectionId))), Mission);
                break;
            case ChuteDeployed chute:
                Add(chute.Time, LogCategory.SystemChange, Format("logRowChute", ("chute", ChuteWord(chute.Chute)), ("result", ChuteResultWord(chute.Result))), Mission);
                break;
            case TipAvailable tip:
                Add(tip.Time, LogCategory.Info, _text.Get("console.logRowTip"), _text.Get("console.logSourceFlightDirector"));
                break;
            case MissionEnded ended:
                Add(ended.Time, LogCategory.Decision, Format("logRowMissionEnded", ("outcome", OutcomeWord(ended.Outcome))), Mission);
                break;
        }
    }

    private void Add(MissionTime at, LogCategory category, string text, string source, AlarmPriority? priority = null) =>
        _pending.Add(new LogEntry(++_sequence, at, category, text, source) { Priority = priority });

    private string Format(string key, params (string Name, string Value)[] values) => _text.Format("console." + key, values);

    private string Crew(CrewId? crew) => crew is { } id && id != CommandRequest.AnyCrew ? _crew.ShortName(id) : Ground;

    private string VehicleName(string vehicleId)
    {
        var id = vehicleId.Length > 0 ? vehicleId : _data.Flight.Vehicles.TargetId ?? string.Empty;
        return _data.Vehicles.FirstOrDefault(v => v.Id == id)?.DisplayName ?? AdapterText.Words(id);
    }

    private string SectionName(string sectionId) => _sectionNames.TryGetValue(sectionId, out var name) ? name : AdapterText.Words(sectionId);

    private string TaskName(TaskId task) => _data.Tasks.TryGet(task, out var definition) ? definition.Name : AdapterText.Words(task.Value);

    /// <summary>The crew task's name; otherwise <c>console.actionTitle_&lt;id&gt;</c> when the strings define it, else the id in words.</summary>
    private string ActionTitle(ActionId action)
    {
        if (_data.Actions.TryGet(action, out var definition) && definition.Definition.TaskId is { } task) return TaskName(task);
        return _text.Find($"console.actionTitle_{action.Value}") ?? AdapterText.Words(action.Value);
    }

    private string FactLabel(FactId fact) => _data.Facts.TryResolve(fact, out var definition) ? definition.Label : AdapterText.Words(fact.Value);

    private string ObjectiveTitle(ObjectiveId id)
    {
        foreach (var objective in _data.Scenario.Mission.Objectives)
        {
            if (objective.Id == id) return objective.Title;
        }
        return AdapterText.Words(id.Value);
    }

    private string ComponentLine(ComponentKnowledge knowledge)
    {
        var name = _componentNames.TryGetValue(knowledge.Component.Value, out var found) ? found : AdapterText.Words(knowledge.Component.Value);
        if (knowledge.State == SimComponentState.ConfirmedNominal) return Format("logRowConfirmedNominal", ("component", name));

        var condition = knowledge.Condition is { Length: > 0 } word
            ? _text.FindWord("status", word) ?? word.Replace('_', ' ')
            : _text.Get("console.logRowConfirmedDamagedWord");
        return knowledge.Variant is { Length: > 0 } variant
            ? Format("logRowConfirmedDamagedVariant", ("component", name), ("condition", condition), ("variant", variant.Replace('_', ' ')))
            : Format("logRowConfirmedDamaged", ("component", name), ("condition", condition));
    }

    /// <summary>The console's phase word (<c>console.phaseDockedOps</c>), else the id in capitals.</summary>
    private string PhaseWord(string phase) => _text.FindWord("phase", phase) ?? phase.Replace('_', ' ').ToUpperInvariant();

    private string ChuteWord(ChuteKind chute) => _text.Get(chute switch
    {
        ChuteKind.Drogue => "console.chuteDrogue",
        ChuteKind.Main => "console.chuteMain",
        _ => throw new ArgumentOutOfRangeException(nameof(chute), chute, null),
    });

    private string ChuteResultWord(ChuteDeployResult result) => _text.Get(result switch
    {
        ChuteDeployResult.Deployed => "console.chuteResultDeployed",
        ChuteDeployResult.AlreadyDeployed => "console.chuteResultAlreadyDeployed",
        ChuteDeployResult.AlreadyFailed => "console.chuteResultAlreadyFailed",
        ChuteDeployResult.TooHigh => "console.chuteResultTooHigh",
        ChuteDeployResult.TooFast => "console.chuteResultTooFast",
        ChuteDeployResult.Landed => "console.chuteResultLanded",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    });

    private string OutcomeWord(MissionOutcomeKind outcome) => _text.Get(outcome switch
    {
        MissionOutcomeKind.Recovered => "console.outcomeRecovered",
        MissionOutcomeKind.AllCrewLost => "console.outcomeAllCrewLost",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    });
}
