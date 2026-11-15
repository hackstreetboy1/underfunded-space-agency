using Usa.Game.Content;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Crew;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Builds the mission console's <see cref="ConsoleSnapshot"/> from a <see cref="MissionSession"/> (UI.md 6.4: view models never
/// query the simulation). Readings come only from knowledge (<see cref="KnowledgeReadouts"/>); truth from the session snapshot
/// is used only for what the ground legitimately sees: MET, phase, completed objectives, annunciator alarms, the crew task
/// slot, crew biomedical telemetry (one visible health number, GDD 5.4) and the relay read-back of remote switches.
/// Objective feasibility is not shown live (its rules read truth); it belongs to grading and the debrief.
/// <para>Feed every drained simulation event to <see cref="Observe"/> (log rows, tip cooldown, crew deaths), then call
/// <see cref="Build(SessionSnapshot, CompressionReading, LinkState)"/> once per presentation frame.</para>
/// </summary>
public sealed class ConsoleSnapshotAdapter
{
    private readonly MissionSession _session;
    private readonly ConsoleLayout _layout;
    private readonly CrewNames _crew;
    private readonly AdapterText _text;
    private readonly KnowledgeReadouts _readouts;
    private readonly KnowledgeForecast _forecast;
    private readonly AlarmText _alarms;
    private readonly EventLogWriter _log;
    private readonly SystemsBoardBuilder _board;
    private readonly Dictionary<CrewId, MissionTime> _deaths = [];
    private readonly long _tipCooldownMs;
    private MissionTime? _tipCooldownUntil;

    /// <param name="text">The game's <c>content/strings.json</c>; without it the adapters' English defaults are used.</param>
    public ConsoleSnapshotAdapter(MissionSession session, ConsoleLayout? layout = null, ITextSource? text = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _layout = layout ?? ConsoleLayout.MissionOne;
        _text = new AdapterText(text);
        _crew = new CrewNames(session.Data.Crew);
        _readouts = new KnowledgeReadouts(session.Data.Facts, session.Knowledge, _crew, _text);
        _forecast = new KnowledgeForecast(session, _readouts, _text);
        _alarms = new AlarmText(session.Data, _crew, _text, _layout);
        _log = new EventLogWriter(session.Data, _crew, _alarms, _text);
        Controls = new RemoteControlMap(session.Data.Actions);
        _board = new SystemsBoardBuilder(session, _layout, _readouts, Controls);
        _tipCooldownMs = (long)Math.Round(session.Data.Hints.CooldownS * 1000);
        MissionTitle = session.Data.Scenario.Mission.Title;
        VehicleName = session.Data.Vehicles.FirstOrDefault(v => v.Cabin is not null)?.DisplayName ?? string.Empty;
    }

    public string MissionTitle { get; }

    public string VehicleName { get; }

    /// <summary>Remote-control bindings, shared with <see cref="ConsoleCommandBridge"/>.</summary>
    public RemoteControlMap Controls { get; }

    public KnowledgeReadouts Readouts => _readouts;

    public void Observe(ISimEvent simEvent)
    {
        ArgumentNullException.ThrowIfNull(simEvent);
        _log.Observe(simEvent);
        switch (simEvent)
        {
            case CrewMemberDied died:
                _deaths.TryAdd(died.Crew, died.Time);
                break;
            case TipWithdrawn { Reason: TipWithdrawReason.Dismissed } dismissed:
                _tipCooldownUntil = dismissed.Time + _tipCooldownMs;
                break;
            case TipAvailable:
                _tipCooldownUntil = null;
                break;
        }
    }

    public ConsoleSnapshot Build(CompressionReading compression, LinkState link = LinkState.Ok) => Build(_session.Snapshot(), compression, link);

    public ConsoleSnapshot Build(SessionSnapshot snapshot, CompressionReading compression, LinkState link = LinkState.Ok)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var now = snapshot.Now;
        var crew = Crew(snapshot);
        var alive = 0;
        foreach (var member in snapshot.Crew)
        {
            if (member.Alive) alive++;
        }

        return new ConsoleSnapshot
        {
            Met = now,
            MissionTitle = MissionTitle,
            VehicleName = VehicleName,
            Phase = AdapterMaps.Phase(snapshot.Phase),
            Compression = new CompressionSnapshot(compression.Rate, AdapterMaps.Cause(compression.Reason))
            {
                CanAdvanceToNextEvent = compression.CanAdvanceToNextEvent,
                IsAdvancingToEvent = compression.IsAdvancingToEvent,
            },
            Link = link,
            Objectives = Objectives(snapshot),
            Resources = Resources(now),
            Forecast = _forecast.Latest(now, alive),
            Crew = crew,
            ActiveTask = Task(snapshot.Task),
            Alarms = snapshot.Alarms.Select(_alarms.Snapshot).ToArray(),
            Tip = Tip(snapshot.Tip, now),
            TipCooldownUntil = _tipCooldownUntil is { } until && until > now ? until : null,
            SystemsBoard = _board.Build(now),
            NewLogEntries = _log.TakePending(),
        };
    }

    /// <summary>Open or completed while the mission runs (abandoned once it ended unfinished): the session's at-risk and
    /// impossible states come from rules that read truth, so the header never shows them.</summary>
    private static ObjectiveSnapshot[] Objectives(SessionSnapshot snapshot)
    {
        var list = new List<ObjectiveSnapshot>();
        foreach (var objective in snapshot.Objectives)
        {
            if (objective.Kind != "primary") continue;
            var state = objective.State == "completed" ? ObjectiveState.Completed
                : snapshot.Ended ? ObjectiveState.Abandoned
                : ObjectiveState.Open;
            list.Add(new ObjectiveSnapshot(objective.Id, ShortLabel(objective.Id), state));
        }
        return [.. list];
    }

    /// <summary>"o1_dock" → "Dock", "o3_eva" → "EVA": the id after its ordinal, title-cased (short words upper-case).</summary>
    private static string ShortLabel(ObjectiveId id)
    {
        var value = id.Value;
        var underscore = value.IndexOf('_', StringComparison.Ordinal);
        var words = (underscore >= 0 ? value[(underscore + 1)..] : value).Replace('_', ' ');
        if (words.Length <= 3) return words.ToUpperInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private ResourceSnapshot[] Resources(MissionTime now)
    {
        var tiles = new ResourceSnapshot[_layout.Tiles.Count];
        for (var i = 0; i < tiles.Length; i++)
        {
            var spec = _layout.Tiles[i];
            var primary = spec.PrimaryFact is { } fact ? _readouts.Read(fact, now) : ReadoutSnapshot.UnknownIn(string.Empty);
            tiles[i] = new ResourceSnapshot(spec.Id, spec.Label, primary)
            {
                Secondary = spec.SecondaryFact is { } secondary ? _readouts.Read(secondary, now) : null,
                Detail = spec.DetailFact is { } detail ? DetailWord(detail) : null,
                Tab = spec.Tab,
                PrimaryFactKey = spec.PrimaryFact?.Value,
                SecondaryFactKey = spec.SecondaryFact?.Value,
            };
        }
        return tiles;
    }

    private string? DetailWord(FactId fact) =>
        _session.Knowledge.Describe(fact).Latest is { Qualitative: { Length: > 0 } word } ? word.ToUpperInvariant() : null;

    private CrewMemberSnapshot[] Crew(SessionSnapshot snapshot)
    {
        var crew = new CrewMemberSnapshot[snapshot.Crew.Count];
        for (var i = 0; i < crew.Length; i++)
        {
            var member = snapshot.Crew[i];
            MissionTime? died = null;
            if (!member.Alive)
            {
                if (!_deaths.TryGetValue(member.Id, out var at)) _deaths[member.Id] = at = snapshot.Now;
                died = at;
            }
            crew[i] = new CrewMemberSnapshot(member.Id, member.Name, _crew.Role(member.Id))
            {
                HealthPercent = member.Alive ? member.Health : 0,
                Activity = Activity(member, snapshot.Task),
                DiedAt = died,
            };
        }
        return crew;
    }

    private string? Activity(CrewView member, TaskSlotView? task)
    {
        if (!member.Alive) return null;
        if (task is { } slot && slot.Assignee == member.Id) return _text.Format("console.crewOnTask", ("task", TaskName(slot.Task)));
        if (member.Location == "eva") return _text.Get("console.crewOnEva");
        return null;
    }

    private ActiveTaskSnapshot? Task(TaskSlotView? slot)
    {
        if (slot is null) return null;
        _session.Data.Tasks.TryGet(slot.Task, out var definition);
        return new ActiveTaskSnapshot(slot.Task, TaskName(slot.Task), slot.Assignee, _crew.FullName(slot.Assignee), slot.StartedAt, slot.EndsAt)
        {
            Progress = slot.Progress,
            Complexity = Math.Clamp(definition?.Complexity ?? 1, 1, 5),
            Interruptible = definition?.Interruptible ?? true,
            UnsafeToInterrupt = definition?.Location == CrewLocation.Eva,
        };
    }

    private string TaskName(TaskId task) => _session.Data.Tasks.TryGet(task, out var definition) ? definition.Name : task.Value;

    private GuidanceTipSnapshot? Tip(TipAvailable? tip, MissionTime now)
    {
        if (tip is null) return null;
        var facts = new List<TipFactSnapshot>(tip.FactRefs.Count);
        foreach (var fact in tip.FactRefs)
        {
            if (!_session.Data.Facts.TryResolve(fact, out var definition)) continue;
            var readout = _readouts.Read(fact, now);
            facts.Add(new TipFactSnapshot(ChipLabel(definition.Label, definition.Unit, readout))
            {
                FactKey = fact.Value,
                Tab = _layout.GroupOf(fact)?.Tab,
                Knowledge = readout.Knowledge,
            });
        }
        return new GuidanceTipSnapshot(tip.ProblemId, AdapterMaps.Stage(tip.Stage), tip.Text, tip.Time) { Facts = facts };
    }

    /// <summary><c>OAMS propellant 212 kg · 06:58</c> when a value is known, else the label alone.</summary>
    private static string ChipLabel(string label, string unit, ReadoutSnapshot readout)
    {
        if (readout.Value is not { } value || readout.ObservedAt is not { } at) return label;
        var minutes = at.Milliseconds / 60_000;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{label} {value:0.#} {unit} · {minutes / 60:00}:{minutes % 60:00}").Replace("  ", " ", StringComparison.Ordinal);
    }
}
