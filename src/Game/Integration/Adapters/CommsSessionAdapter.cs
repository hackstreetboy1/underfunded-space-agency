using Usa.Game.Comms;
using Usa.Game.Content;
using Usa.Simulation.Attitude;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;
using SimComponentState = Usa.Simulation.Knowledge.ComponentKnowledgeState;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// <see cref="ICommsSessionPort"/> over a <see cref="MissionSession"/>. Feed every drained event to <see cref="Observe"/> and call
/// <see cref="Flush"/> once the drain is done (the host does both per frame); comms events are raised in simulation order.
/// <para>Observation batches: consecutive crew observations by one observer at one MET form one
/// <see cref="CommsObservationsRecorded"/>, with the command that asked for them (the completed task, or the pending glance
/// read of that crew member). Crew reports (GDD 5.1, raised on their own): a confirmed damaged component or a conflicting
/// reading in a batch (those observations leave the batch), the crewed vehicle's rate crossing a threshold upward, a warning
/// or master alarm, and a crew member's own health band worsening.</para>
/// </summary>
public sealed class CommsSessionAdapter : ICommsSessionPort
{
    /// <summary>
    /// How long after its ready time a glance read may still claim observations. The session reports a glance at the first
    /// one-second step at or after it is ready, or never (the reader died, went outside, or the gauge gave nothing).
    /// </summary>
    public const long GlanceGraceMs = 2000;

    private readonly MissionSession _session;
    private readonly CrewNames _names;
    private readonly AlarmText _alarms;
    private readonly List<CrewId> _crewOrder;
    private readonly List<PendingGlance> _glances = [];
    private readonly List<Observation> _batch = [];
    private readonly HashSet<FactId> _conflicts = [];
    private readonly List<CommsSessionEvent> _queue = [];
    private CommandId? _batchOrigin;
    private MissionTime _batchOriginTime;
    private CrewId _batchOriginCrew;
    private readonly FactId[] _rateFacts;
    private readonly string _crewedVehicle;
    private readonly CommandText _commands;

    /// <param name="strings">Words for <see cref="DescribeCommand"/> (<c>content/strings.json</c>); null uses the vehicle and fact names only.</param>
    public CommsSessionAdapter(MissionSession session, ITextSource? strings = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _commands = new CommandText(session.Data, new AdapterText(strings));
        _names = new CrewNames(session.Data.Crew);
        _alarms = new AlarmText(session.Data, _names);
        _crewOrder = [.. session.Data.Scenario.Mission.Crew.OrderBy(c => c)];
        _crewedVehicle = session.Data.Flight.Vehicles.CrewedId;
        _rateFacts = session.Data.Facts.Facts.Where(f => f.Id.Value.StartsWith($"{_crewedVehicle}.rate.", StringComparison.Ordinal))
            .Select(f => f.Id).ToArray();
    }

    public event Action<CommsSessionEvent>? EventRaised;

    public MissionTime Now => _session.Now;

    public string Phase => _session.State.TryGet("mission.phase", out var phase) && phase.Kind == StateValueKind.Text ? phase.AsText() : string.Empty;

    public IReadOnlyList<CommsCrewStatus> Crew
    {
        get
        {
            var list = new List<CommsCrewStatus>(_crewOrder.Count);
            foreach (var id in _crewOrder)
            {
                var alive = Bool($"crew.{id.Value}.alive");
                var activity = Text($"crew.{id.Value}.activity");
                list.Add(new CommsCrewStatus(id, alive, Bool($"crew.{id.Value}.busy"), alive ? Number($"crew.{id.Value}.health") : 0,
                    activity.Length > 0 ? new TaskId(activity) : null));
            }
            return list;
        }
    }

    public IReadOnlyList<CatalogAction> AllowedActions => _session.Actions.AllowedIn(Phase);

    public IReadOnlyList<FactId> FactsKnownBy(CrewId crew) => _session.Knowledge.FactsKnownBy(crew);

    public bool Knows(CrewId crew, FactId fact) => _session.Knowledge.Knows(crew, fact);

    public FactTemplateResult ResolveFacts(string template, CrewId speaker) => _session.FactTemplates.Resolve(template, speaker, _session.Now);

    public string DescribeTask(TaskId task) => _session.Data.Tasks.TryGet(task, out var definition) ? definition.Name : task.Value;

    public string DescribeCommand(CommandRequest command) => _commands.Describe(command);

    public ValidationResult Submit(CommandRequest request) => _session.Submit(request);

    /// <summary>True while <paramref name="crew"/> is reading a gauge on request (between the glance command and its report).</summary>
    public bool IsReadingGauge(CrewId crew)
    {
        var now = _session.Now;
        for (var i = 0; i < _glances.Count; i++)
        {
            var glance = _glances[i];
            if (glance.Reader == crew && glance.StartedAt <= now && now < glance.ReadyAt) return true;
        }
        return false;
    }

    /// <summary>One drained simulation event, in drain order.</summary>
    public void Observe(ISimEvent simEvent)
    {
        ArgumentNullException.ThrowIfNull(simEvent);
        switch (simEvent)
        {
            case ObservationRecorded recorded:
                AddObservation(recorded.Observation);
                break;
            case KnowledgeChanged { Current: KnowledgeState.Conflicting } conflict:
                _conflicts.Add(conflict.Fact);
                break;
            case CommandExecuted { Kind: ActionKind.GlanceRead } glance:
                TrackGlance(glance);
                break;
            case CrewTaskStarted started:
                CloseBatch();
                Enqueue(new CommsTaskEvent(started.Time, CommsTaskEventKind.Started, started.Origin, started.Task, started.Assignee));
                break;
            case CrewTaskCompleted completed:
                TaskEnded(completed.Time, completed.Assignee, completed.Origin);
                Enqueue(new CommsTaskEvent(completed.Time, CommsTaskEventKind.Completed, completed.Origin, completed.Task, completed.Assignee));
                break;
            case CrewTaskFailed failed:
                TaskEnded(failed.Time, failed.Assignee, failed.Origin);
                Enqueue(new CommsTaskEvent(failed.Time, CommsTaskEventKind.Failed, failed.Origin, failed.Task, failed.Assignee));
                break;
            case CrewTaskStopped stopped:
                CloseBatch();
                Enqueue(new CommsTaskEvent(stopped.Time, CommsTaskEventKind.Stopped, stopped.Origin, stopped.Task, stopped.Assignee,
                    stopped.Reason.ToString()));
                break;
            case RateThresholdCrossed { Rising: true } rate when rate.VehicleId == _crewedVehicle && Reporter() is { } reporter:
                Enqueue(new CommsCrewReport(rate.Time, reporter, FormattableString.Invariant($"spin rate rising past {rate.ThresholdDegPerSec:0} °/s"), _rateFacts,
                    Critical: rate.ThresholdDegPerSec >= 10));
                break;
            case AlarmRaised { Level: >= AlarmLevel.Warning } alarm when Reporter() is { } reporter:
                Enqueue(new CommsCrewReport(alarm.Time, reporter, $"alarm: {_alarms.Describe(alarm.AlarmId, alarm.Subject).Text}", [],
                    Critical: alarm.Level == AlarmLevel.Master));
                break;
            case CrewHealthBandChanged band when band.Current > band.Previous && band.Current != HealthBand.Deceased:
                Enqueue(new CommsCrewReport(band.Time, band.Crew, $"feeling {AdapterMaps.BandWord(band.Current)}",
                    [_session.Data.Facts.CrewConditionFact(band.Crew)], Critical: band.Current >= HealthBand.Impaired));
                break;
        }
    }

    /// <summary>Closes the open observation batch and raises everything queued since the last flush, in order.</summary>
    public void Flush()
    {
        CloseBatch();
        DropExpiredGlances(_session.Now);
        if (_queue.Count == 0) return;
        var events = _queue.ToArray();
        _queue.Clear();
        foreach (var sessionEvent in events) EventRaised?.Invoke(sessionEvent);
    }

    /// <summary>A task's result observations are published either side of its end event; both orders get the task's origin.</summary>
    private void TaskEnded(MissionTime time, CrewId assignee, CommandId origin)
    {
        if (_batch.Count > 0 && _batch[0].Observer == assignee && _batch[0].Time == time)
        {
            _batchOrigin = origin;
            CloseBatch();
            return;
        }
        CloseBatch();
        _batchOrigin = origin;
        _batchOriginTime = time;
        _batchOriginCrew = assignee;
    }

    private void AddObservation(Observation observation)
    {
        if (observation.Observer is not { } observer || observation.Method is ObservationMethod.Downlink or ObservationMethod.Model) return;
        if (_batch.Count > 0 && (_batch[0].Observer != observer || _batch[0].Time != observation.Time)) CloseBatch();
        if (_batch.Count == 0 && _batchOrigin is not null && (_batchOriginCrew != observer || _batchOriginTime != observation.Time)) _batchOrigin = null;
        _batch.Add(observation);
    }

    private void TrackGlance(CommandExecuted glance)
    {
        var delayMs = _session.Actions.TryGet(glance.Action, out var action) && action.Definition.Glance is { } definition
            ? (long)Math.Round(definition.DelayS * 1000)
            : 0;
        DropExpiredGlances(glance.Time);
        _glances.Add(new PendingGlance(glance.Assignee, glance.Command, glance.Time, glance.Time + delayMs));
    }

    // A glance that never reported must not lend its command to a later glance by the same reader.
    private void DropExpiredGlances(MissionTime now)
    {
        for (var i = _glances.Count - 1; i >= 0; i--)
        {
            if (_glances[i].ReadyAt + GlanceGraceMs < now) _glances.RemoveAt(i);
        }
    }

    private void CloseBatch()
    {
        if (_batch.Count == 0) return;
        var observer = _batch[0].Observer!.Value;
        var time = _batch[0].Time;
        var origin = _batchOrigin ?? TakeGlanceOrigin(observer, time);

        var reported = new List<Observation>();
        var routine = new List<Observation>();
        foreach (var observation in _batch)
        {
            (IsSignificant(observation) ? reported : routine).Add(observation);
        }
        if (routine.Count > 0) Enqueue(new CommsObservationsRecorded(time, observer, routine, origin));
        if (reported.Count > 0)
        {
            var facts = reported.Select(o => o.Fact).Distinct().ToArray();
            Enqueue(new CommsCrewReport(time, observer, Summary(reported), facts, Critical: false));
        }
        _batch.Clear();
        _conflicts.Clear();
        _batchOrigin = null;
    }

    private bool IsSignificant(Observation observation)
    {
        if (_conflicts.Contains(observation.Fact)) return true;
        if (!_session.Data.Facts.TryResolve(observation.Fact, out var fact) || fact.ConfirmsComponent is not { } component) return false;
        return _session.Knowledge.ComponentOf(component).State == SimComponentState.ConfirmedDamaged && !fact.IsNominalText(observation.Qualitative);
    }

    private string Summary(List<Observation> reported)
    {
        var parts = new List<string>(reported.Count);
        foreach (var observation in reported)
        {
            var label = _session.Data.Facts.TryResolve(observation.Fact, out var fact) ? fact.Label : observation.Fact.Value;
            parts.Add(_conflicts.Contains(observation.Fact) ? $"conflicting reading: {label}" : $"damage found: {label} {observation.Qualitative}");
        }
        return string.Join("; ", parts);
    }

    private CommandId? TakeGlanceOrigin(CrewId observer, MissionTime time)
    {
        DropExpiredGlances(time);
        for (var i = 0; i < _glances.Count; i++)
        {
            var glance = _glances[i];
            if (glance.ReadyAt > time) continue;
            if (glance.Reader is { } reader && reader != observer && reader != CommandRequest.AnyCrew) continue;
            _glances.RemoveAt(i);
            return glance.Origin;
        }
        return null;
    }

    /// <summary>Who speaks up on their own: the first living crew member in the cabin, idle ones first.</summary>
    private CrewId? Reporter()
    {
        CrewId? busy = null;
        foreach (var id in _crewOrder)
        {
            if (!Bool($"crew.{id.Value}.alive") || Text($"crew.{id.Value}.location") == "eva") continue;
            if (!Bool($"crew.{id.Value}.busy")) return id;
            busy ??= id;
        }
        return busy;
    }

    private void Enqueue(CommsSessionEvent sessionEvent) => _queue.Add(sessionEvent);

    private bool Bool(string path) => _session.State.TryGet(path, out var value) && value.Kind == StateValueKind.Bool && value.AsBool();

    private double Number(string path) =>
        _session.State.TryGet(path, out var value) && value.Kind is StateValueKind.Number ? value.AsNumber() : 0;

    private string Text(string path) => _session.State.TryGet(path, out var value) && value.Kind == StateValueKind.Text ? value.AsText() : string.Empty;

    private sealed record PendingGlance(CrewId? Reader, CommandId Origin, MissionTime StartedAt, MissionTime ReadyAt);
}
