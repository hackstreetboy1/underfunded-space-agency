using Usa.Game.Content;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Map.Planning;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// The burn planner's engine list (UI.md 4.1): the engines the session fires on command (<c>session.json</c> <c>burns</c>,
/// <see cref="MissionSessionConfig.BurnEngines"/>) plus the flight's retro sequence. A translation engine of the crewed vehicle
/// (its listed thrusters, or every ungrouped thruster pushing along +X, as the session fires them) is usable while coasting
/// undocked with its section attached; a docked main engine while coasting docked. Each option's id is the session burn id
/// (<c>session.burn.&lt;id&gt;.*</c>), the retro sequence's <see cref="PlannerEngineOption.RetroSequence"/>.
/// Propellant comes from knowledge only (X8): the tank's fact as read, reported, estimated or downlinked as a band.
/// </summary>
/// <remarks>
/// <see cref="Refresh"/> replaces the planner's list only when something the panel shows changes (availability, reason or
/// propellant knowledge), so the true tank draining under attitude firing never keeps the preview stale. The true load in
/// <see cref="PlannerEngineOption.Engine"/> is refreshed with those changes; the planner never previews or displays it.
/// </remarks>
public sealed class PlannerEngineSource
{
    private readonly IStateQuery _state;
    private readonly FlightTrack _track;
    private readonly KnowledgeReadouts _readouts;
    private readonly RetroSequenceEngine _retro;
    private readonly SessionEngine[] _engines;
    private readonly RetroSpec? _retroSpec;
    private readonly Words _words;
    private IReadOnlyList<PlannerEngineOption>? _shown;

    public PlannerEngineSource(MissionSession session, FlightTrack track, KnowledgeReadouts readouts, ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(readouts);
        ArgumentNullException.ThrowIfNull(text);
        _state = session.State;
        _track = track;
        _readouts = readouts;
        _words = new Words(text);
        _retro = new RetroSequenceEngine(session.Data.Flight.RetroSequence);
        var facts = session.Data.Facts;
        var crewedId = session.Data.Flight.Vehicles.CrewedId;
        var engines = new List<SessionEngine>();
        foreach (var burn in session.Data.SessionConfig.BurnEngines)
        {
            if (session.Data.Vehicles.FirstOrDefault(v => v.Id == burn.Vehicle) is not { } vehicle) continue;
            switch (burn.Kind)
            {
                // The planner previews the crewed vehicle; another vehicle's translation burn is not its to plan.
                case BurnEngineKind.Translation when vehicle.Id == crewedId && TranslationThrusters(vehicle, burn, facts) is { } spec:
                    engines.Add(new SessionEngine(burn.Id, burn.Kind, spec, _words.Translation, null));
                    break;
                case BurnEngineKind.DockedMainEngine when MainEngine(vehicle, burn, facts) is { } spec:
                    engines.Add(new SessionEngine(burn.Id, burn.Kind, spec, _words.Engine(vehicle.DisplayName), _words.NeedsDocked(vehicle.DisplayName)));
                    break;
            }
        }
        _engines = [.. engines];
        if (session.Data.Vehicles.FirstOrDefault(v => v.Id == crewedId) is { } crewed) _retroSpec = RetroRockets(crewed);
    }

    /// <summary>The list the planner shows now.</summary>
    public IReadOnlyList<PlannerEngineOption> Build(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var options = new List<PlannerEngineOption>(_engines.Length + 1);
        var coasting = _track.Phase == FlightPhase.Coast;
        foreach (var engine in _engines)
        {
            var spec = engine.Spec;
            var reason = !coasting ? _words.NotInOrbit
                : engine.Kind == BurnEngineKind.Translation
                    ? _track.Docked ? _words.NotWhileDocked : !Attached(snapshot, spec.VehicleId, spec.Section) ? _words.Jettisoned : null
                    : !_track.Docked ? engine.NeedsDocked : null;
            options.Add(Option(engine.Id, engine.Label, spec, snapshot.Now, reason));
        }
        if (_retroSpec is { } retro && _retro.Sequence.Rockets.Count > 0)
        {
            var reason = !coasting ? _words.NotInOrbit
                : AnyFired(retro) ? _words.Fired
                : !Attached(snapshot, retro.VehicleId, retro.Section) ? _words.Jettisoned
                : null;
            options.Add(new PlannerEngineOption(PlannerEngineOption.RetroSequence, _words.Retro, _retro, reason is null, reason));
        }
        return options;
    }

    /// <summary>Gives <paramref name="planner"/> the current list when what it shows changed; true when it did.</summary>
    public bool Refresh(BurnPlannerModel planner, SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(planner);
        var options = Build(snapshot);
        if (_shown is { } shown && SameShown(shown, options)) return false;
        _shown = options;
        planner.SetEngines(options);
        return true;
    }

    /// <summary>Maps a fact readout to what the planner knows of a tank; <paramref name="bands"/> turn a downlink word into a range.</summary>
    public static PropellantKnowledge ToPropellant(ReadoutSnapshot readout, IReadOnlyList<GroundBand> bands, double capacityKg)
    {
        ArgumentNullException.ThrowIfNull(readout);
        ArgumentNullException.ThrowIfNull(bands);
        var source = readout.Source;
        switch (readout.Knowledge)
        {
            case ReadoutKnowledge.Known when readout.Value is { } value && readout.ObservedAt is { } at:
                return PropellantKnowledge.Reported(value, at, source ?? string.Empty);
            case ReadoutKnowledge.Stale when readout.Value is { } value && readout.ObservedAt is { } at:
                return PropellantKnowledge.Reported(value, at, source ?? string.Empty, stale: true);
            case ReadoutKnowledge.Conflict when readout.Value is { } value:
                return PropellantKnowledge.Estimated(value, readout.ConflictValue ?? value, source, readout.ObservedAt);
            case ReadoutKnowledge.Estimated when readout.Value is { } value:
                return PropellantKnowledge.Estimated(readout.RangeMin ?? value, readout.RangeMax ?? value, source, readout.ObservedAt);
            case ReadoutKnowledge.GroundBand when readout.GroundBand is { } word && BandRange(bands, word, capacityKg) is { } range:
                return PropellantKnowledge.Estimated(range.LowKg, range.HighKg, source, readout.ObservedAt);
            default:
                return PropellantKnowledge.Unknown;
        }
    }

    private PlannerEngineOption Option(string id, string label, EngineSpec spec, MissionTime now, string? reason)
    {
        var trueKg = Number($"{spec.TankId}.{ComponentFields.PropKg}");
        var engine = new ConstantThrustEngine(spec.ThrustN, spec.IspS, trueKg);
        var known = spec.Fact is { } fact ? ToPropellant(_readouts.Read(fact.Id, now), fact.Downlink?.Bands ?? [], spec.CapacityKg) : PropellantKnowledge.Unknown;
        return new PlannerEngineOption(id, label, engine, reason is null, reason, known);
    }

    private static (double LowKg, double HighKg)? BandRange(IReadOnlyList<GroundBand> bands, string word, double capacityKg)
    {
        for (var i = 0; i < bands.Count; i++)
        {
            if (!string.Equals(bands[i].Word, word, StringComparison.Ordinal)) continue;
            var low = bands[i].From ?? 0;
            var high = capacityKg;
            for (var j = 0; j < bands.Count; j++)
            {
                if (bands[j].From is { } from && from > low && from < high) high = from;
            }
            return (low, high);
        }
        return null;
    }

    private static bool SameShown(IReadOnlyList<PlannerEngineOption> a, IReadOnlyList<PlannerEngineOption> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (Shown(a[i]) != Shown(b[i])) return false;
        }
        return true;
    }

    private static PlannerEngineOption Shown(PlannerEngineOption option) =>
        option.Engine is ConstantThrustEngine constant ? option with { Engine = constant with { PropellantKg = 0 } } : option;

    private static bool Attached(SessionSnapshot snapshot, string vehicleId, string section)
    {
        foreach (var vehicle in snapshot.Vehicles)
        {
            if (vehicle.Id != vehicleId) continue;
            foreach (var view in vehicle.Sections)
            {
                if (view.Id == section) return view.Attached;
            }
        }
        return false;
    }

    private bool AnyFired(RetroSpec retro)
    {
        foreach (var rocket in retro.Rockets)
        {
            if (_state.TryGet($"{rocket.Value}.{ComponentFields.Fired}", out var fired) && fired.Kind == StateValueKind.Bool && fired.AsBool()) return true;
        }
        return false;
    }

    private double Number(string path) => _state.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? value.AsNumber() : 0;

    /// <summary>The burn's listed thrusters, or by default every ungrouped thruster pushing along +X (the session's own rule), as one engine.</summary>
    private static EngineSpec? TranslationThrusters(VehicleDefinition vehicle, BurnEngineDefinition burn, FactCatalog facts)
    {
        double thrustN = 0, massFlow = 0;
        ComponentDefinition? first = null;
        foreach (var component in vehicle.Components)
        {
            if (component.Thruster is not { } thruster || !(thruster.IspS > 0)) continue;
            var fires = burn.Thrusters is { Count: > 0 } listed
                ? listed.Contains(component.Id)
                : thruster.Group is null && thruster.DirectionUnit.Count > 0 && thruster.DirectionUnit[0] > 0.99;
            if (!fires) continue;
            first ??= component;
            thrustN += thruster.ThrustN;
            massFlow += thruster.ThrustN / thruster.IspS;
        }
        if (first?.Thruster is not { } spec || massFlow <= 0) return null;
        return Spec(vehicle, first.Section, spec.Tank, thrustN, thrustN / massFlow, facts);
    }

    /// <summary>The burn's named engine, or the vehicle's first engine (the session's own rule).</summary>
    private static EngineSpec? MainEngine(VehicleDefinition vehicle, BurnEngineDefinition burn, FactCatalog facts)
    {
        var component = vehicle.Components.FirstOrDefault(c => c.Engine is not null && (burn.Engine is not { } named || c.Id == named));
        return component?.Engine is { } engine ? Spec(vehicle, component.Section, engine.Tank, engine.ThrustN, engine.IspS, facts) : null;
    }

    private static EngineSpec Spec(VehicleDefinition vehicle, string section, ComponentId tank, double thrustN, double ispS, FactCatalog facts)
    {
        var capacity = vehicle.Components.FirstOrDefault(c => c.Id == tank)?.Tank?.CapacityKg ?? 0;
        var fact = facts.Facts.FirstOrDefault(f => f.SourceComponent == tank && f.Kind == FactValueKind.Number);
        return new EngineSpec(vehicle.Id, section, tank, thrustN, ispS, capacity, fact);
    }

    private static RetroSpec? RetroRockets(VehicleDefinition vehicle)
    {
        var rockets = vehicle.Components.Where(c => c.RetroRocket is not null).OrderBy(c => c.Id.Value, StringComparer.Ordinal).ToArray();
        return rockets.Length == 0 ? null : new RetroSpec(vehicle.Id, rockets[0].Section, [.. rockets.Select(r => r.Id)]);
    }

    private sealed record EngineSpec(string VehicleId, string Section, ComponentId TankId, double ThrustN, double IspS, double CapacityKg, FactDefinition? Fact);

    /// <param name="NeedsDocked">The unavailable reason of a docked main engine while apart; null for a translation engine.</param>
    private sealed record SessionEngine(string Id, BurnEngineKind Kind, EngineSpec Spec, string Label, string? NeedsDocked);

    private sealed record RetroSpec(string VehicleId, string Section, IReadOnlyList<ComponentId> Rockets);

    /// <summary>Engine names and unavailable reasons (<c>planner.engine*</c>, <c>planner.reason*</c>).</summary>
    private sealed class Words(ITextSource text)
    {
        public string Translation { get; } = text.Get("planner.engineTranslation");

        public string Retro { get; } = text.Get("planner.engineRetro");

        public string NotWhileDocked { get; } = text.Get("planner.reasonDocked");

        public string Jettisoned { get; } = text.Get("planner.reasonJettisoned");

        public string Fired { get; } = text.Get("planner.reasonFired");

        public string NotInOrbit { get; } = text.Get("planner.reasonNotCoasting");

        public string Engine(string vehicleName) => text.Format("planner.engineTarget", new Dictionary<string, string> { ["target"] = vehicleName });

        public string NeedsDocked(string vehicleName) => text.Format("planner.reasonNeedsDocked", new Dictionary<string, string> { ["target"] = vehicleName });
    }
}
