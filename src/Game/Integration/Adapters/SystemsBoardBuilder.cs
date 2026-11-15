using Usa.Game.Presentation.Console.Snapshots;
using Usa.Simulation.Crew.Health;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Systems-board groups (UI.md 3.9): fact readouts grouped by <see cref="ConsoleLayout"/>, the crewed vehicle's load list,
/// and every switch, breaker, thruster ring and parachute as a control. A control is <see cref="ControlAuthority.Remote"/>
/// only when a remote action reaches it (<see cref="RemoteControlMap"/>); its position is the relay read-back the ground
/// commands. Crew-only positions and every condition come from knowledge, never from the vehicle.
/// </summary>
public sealed class SystemsBoardBuilder
{
    private const string RatesGroupId = "rates";

    private readonly ConsoleLayout _layout;
    private readonly KnowledgeReadouts _readouts;
    private readonly KnowledgeBase _knowledge;
    private readonly IStateQuery _state;
    private readonly RemoteControlMap _remote;
    private readonly List<(FactDefinition Fact, FactGroupSpec Group)> _readoutFacts = [];
    private readonly List<ControlSpec> _controls = [];
    private readonly List<LoadSpecRow> _loads = [];
    private readonly Dictionary<ComponentId, (ControlImpact On, ControlImpact Off)> _impacts = [];
    private readonly GaugeScale _rateGauge;

    public SystemsBoardBuilder(MissionSession session, ConsoleLayout layout, KnowledgeReadouts readouts, RemoteControlMap remote)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(readouts);
        ArgumentNullException.ThrowIfNull(remote);
        _layout = layout;
        _readouts = readouts;
        _knowledge = session.Knowledge;
        _state = session.State;
        _remote = remote;
        _rateGauge = RateGauge(session.Data);

        foreach (var fact in session.Data.Facts.Facts)
        {
            if (fact.Kind == FactValueKind.Number && layout.GroupOf(fact.Id) is { } group) _readoutFacts.Add((fact, group));
        }

        var switchedBy = new Dictionary<ComponentId, ComponentDefinition>();
        foreach (var vehicle in session.Data.Vehicles)
        {
            foreach (var component in vehicle.Components)
            {
                if ((component.Breaker ?? component.Switch) is { To: { } to }) switchedBy.TryAdd(to, component);
            }
        }
        foreach (var vehicle in session.Data.Vehicles)
        {
            foreach (var component in vehicle.Components)
            {
                if (Control(component) is { } control) _controls.Add(control);
                if (vehicle.Cabin is not null && component.Load is { } load)
                {
                    switchedBy.TryGetValue(component.Id, out var breaker);
                    _loads.Add(new LoadSpecRow(component, load, breaker is null ? null : Control(breaker)));
                }
            }
        }
    }

    public IReadOnlyList<SystemsGroupSnapshot> Build(MissionTime now)
    {
        var readouts = new Dictionary<SystemsTab, List<ReadoutGroupSnapshot>>();
        var groupRows = new Dictionary<string, (FactGroupSpec Spec, List<SystemReadoutSnapshot> Rows)>(StringComparer.Ordinal);
        var groupOrder = new List<string>();
        foreach (var (fact, group) in _readoutFacts)
        {
            if (!groupRows.TryGetValue(group.GroupId, out var entry))
            {
                entry = (group, []);
                groupRows[group.GroupId] = entry;
                groupOrder.Add(group.GroupId);
            }
            entry.Rows.Add(new SystemReadoutSnapshot(fact.Id.Value, fact.Label, _readouts.Read(fact.Id, now))
            {
                Gauge = group.GroupId == RatesGroupId ? _rateGauge : null,
            });
        }
        foreach (var id in groupOrder)
        {
            var (spec, rows) = groupRows[id];
            if (!readouts.TryGetValue(spec.Tab, out var list)) readouts[spec.Tab] = list = [];
            list.Add(new ReadoutGroupSnapshot(spec.GroupId, spec.Title, rows));
        }

        var controls = new Dictionary<SystemsTab, List<ControlSnapshot>>();
        foreach (var control in _controls)
        {
            if (!controls.TryGetValue(control.Tab, out var list)) controls[control.Tab] = list = [];
            list.Add(Snapshot(control));
        }

        var loads = new List<LoadRowSnapshot>(_loads.Count);
        foreach (var row in _loads)
        {
            var knowledge = _knowledge.ComponentOf(row.Component.Id);
            var breaker = row.Breaker is null ? null : Snapshot(row.Breaker);
            loads.Add(new LoadRowSnapshot(row.Component.Id, row.Component.Name, AdapterMaps.Priority(row.Load.Priority), row.Load.PowerW,
                row.Load.HeatW ?? row.Load.PowerW)
            {
                IsOn = breaker?.IsOn,
                Condition = AdapterMaps.Condition(knowledge.State, knowledge.Condition),
                Switch = breaker,
            });
        }

        var groups = new List<SystemsGroupSnapshot>();
        foreach (var tab in Enum.GetValues<SystemsTab>())
        {
            readouts.TryGetValue(tab, out var tabReadouts);
            controls.TryGetValue(tab, out var tabControls);
            var tabLoads = tab == SystemsTab.Power ? loads : [];
            if (tabReadouts is null && tabControls is null && tabLoads.Count == 0) continue;
            groups.Add(new SystemsGroupSnapshot(tab)
            {
                ReadoutGroups = tabReadouts ?? [],
                Controls = tabControls ?? [],
                Loads = tabLoads,
            });
        }
        return groups;
    }

    private ControlSnapshot Snapshot(ControlSpec control)
    {
        var remote = _remote.IsRemote(control.Component.Id);
        var snapshot = new ControlSnapshot(control.Component.Id, control.Label, remote ? ControlAuthority.Remote : ControlAuthority.CrewOnly)
        {
            Kind = control.Kind,
            Guarded = remote && _remote.IsGuarded(control.Component.Id),
            OnLabel = control.OnLabel,
            OffLabel = control.OffLabel,
        };
        if (remote)
        {
            var (on, off) = ImpactsOf(control.Component.Id);
            snapshot = snapshot with { OnImpact = on, OffImpact = off };
        }
        if (remote && control.Kind == ControlKind.Switch && _state.TryGet(control.PositionPath, out var closed) && closed.Kind == StateValueKind.Bool)
            snapshot = snapshot with { IsOn = closed.AsBool() };
        return snapshot;
    }

    // The confirmation dialog's effects (UI.md 5.2): the remote action's impact keys for each position, worked out once.
    private (ControlImpact On, ControlImpact Off) ImpactsOf(ComponentId component)
    {
        if (!_impacts.TryGetValue(component, out var impacts))
        {
            impacts = (_remote.ImpactOf(component, on: true), _remote.ImpactOf(component, on: false));
            _impacts[component] = impacts;
        }
        return impacts;
    }

    /// <summary>
    /// The body-rate gauges' scale from documented limits (crew.json, health.json), not truth: caution where the crew can no
    /// longer keep up, critical where the spin starts to harm them (the annunciator's own thresholds), the arc to three times
    /// the caution limit.
    /// </summary>
    private static GaugeScale RateGauge(MissionData data)
    {
        var caution = data.Crew.Performance.RateImpairmentThresholdDps;
        double? critical = null;
        for (var dps = 0; dps <= 1000; dps++)
        {
            if (data.Health.DamagePerS(HealthCause.AngularRate, dps) <= 0) continue;
            critical = dps;
            break;
        }
        return new GaugeScale(Math.Max(caution * 3, (critical ?? 0) * 1.5), caution, critical);
    }

    private static ControlSpec? Control(ComponentDefinition component)
    {
        switch (component.Family)
        {
            case ComponentFamily.Breaker or ComponentFamily.Switch when component.Control != ControlAccess.None:
            {
                var to = (component.Breaker ?? component.Switch)?.To;
                return new ControlSpec(component, component.Name, ControlKind.Switch, ConsoleLayout.TabOf(component.Id, to), $"{component.Id.Value}.closed");
            }
            case ComponentFamily.ThrusterGroup when component.Control != ControlAccess.None:
                return new ControlSpec(component, component.Name, ControlKind.Switch, SystemsTab.PropAttitude, $"{component.Id.Value}.armed",
                    "ARMED", "SEALED");
            case ComponentFamily.Parachute when component.Control != ControlAccess.None:
                return new ControlSpec(component, $"Deploy {component.Name.ToLowerInvariant()}", ControlKind.Button, SystemsTab.StructureLanding,
                    $"{component.Id.Value}.deployed");
            default:
                return null;
        }
    }

    private sealed record ControlSpec(
        ComponentDefinition Component, string Label, ControlKind Kind, SystemsTab Tab, string PositionPath, string? OnLabel = null, string? OffLabel = null);

    private sealed record LoadSpecRow(ComponentDefinition Component, LoadSpec Load, ControlSpec? Breaker);
}
