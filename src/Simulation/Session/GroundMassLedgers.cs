using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Knowledge.Ledger;
using Usa.Simulation.Ports;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Session;

/// <summary>
/// The ground's mass of every vehicle (X8), from paper and knowledge only: one <see cref="GroundMassLedger"/> per vehicle,
/// built from its data (sections' dry masses, payloads, unburnt retro propellant, and its tanks: a tank with a propellant fact
/// is known through that fact and its gauge, any other tank as its loaded contents), fed with what the ground knows changed
/// (<see cref="Events"/>). Serves <see cref="GroundTracking"/> the stack's mass while docked.
/// </summary>
internal sealed class GroundMassLedgers : IGroundMassSource
{
    private readonly Dictionary<string, (VehicleDefinition Vehicle, GroundMassLedger Ledger)> _ledgers = new(StringComparer.Ordinal);
    private readonly KnowledgeBase _knowledge;
    private readonly IStateQuery _state;
    private readonly BurnController _burns;
    private readonly FlightSystem _flight;

    /// <param name="dryTolerance">1σ relative error of design masses (<c>navigation.json</c> <c>ledgerFraction</c>).</param>
    public GroundMassLedgers(
        IReadOnlyList<VehicleDefinition> vehicles, FactCatalog facts, KnowledgeBase knowledge, IStateQuery state, BurnController burns,
        FlightSystem flight, double dryTolerance)
    {
        _knowledge = knowledge;
        _state = state;
        _burns = burns;
        _flight = flight;
        foreach (var vehicle in vehicles.OrderBy(v => v.Id, StringComparer.Ordinal))
            _ledgers[vehicle.Id] = (vehicle, new GroundMassLedger(SpecOf(vehicle, facts, dryTolerance), facts));
    }

    /// <summary>The ledger's estimate for one vehicle now; null for an unknown vehicle. Allocates.</summary>
    public GroundMassEstimate? Estimate(string vehicleId) =>
        _ledgers.TryGetValue(vehicleId, out var entry) ? entry.Ledger.Estimate(_knowledge, Events(vehicleId)) : null;

    public bool TryGetMass(string vehicleId, out double lowKg, out double bestKg, out double highKg)
    {
        lowKg = bestKg = highKg = 0;
        if (Estimate(vehicleId) is not { } own) return false;
        (lowKg, bestKg, highKg) = (own.Mass.LowKg, own.Mass.BestKg, own.Mass.HighKg);
        var partner = !_flight.Docked ? null
            : vehicleId == _flight.CrewedId ? _flight.TargetId
            : vehicleId == _flight.TargetId ? _flight.CrewedId
            : null;
        if (partner is not null && Estimate(partner) is { } other)
        {
            lowKg += other.Mass.LowKg;
            bestKg += other.Mass.BestKg;
            highKg += other.Mass.HighKg;
        }
        return true;
    }

    /// <summary>
    /// What the ground knows changed a vehicle's mass: jettisoned sections, fired retro rockets (their propellant is a spent
    /// dry-mass id) and every commanded burn's engine-spec draw from its tanks.
    /// </summary>
    /// <exception cref="ArgumentException">Unknown vehicle.</exception>
    public KnownMassEvents Events(string vehicleId)
    {
        if (!_ledgers.TryGetValue(vehicleId, out var entry)) throw new ArgumentException($"Unknown vehicle '{vehicleId}'.", nameof(vehicleId));
        var vehicle = entry.Vehicle;
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in vehicle.Sections)
        {
            if (_state.TryGet($"{StatePaths.Vehicle}{vehicleId}.section.{section.Id}.attached", out var attached) &&
                attached.Kind == StateValueKind.Bool && !attached.AsBool())
                removed.Add(section.Id);
        }
        var tanks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in vehicle.Components)
        {
            if (component.Tank is not null) tanks.Add(component.Id.Value);
            if (component.RetroRocket is not null && _state.TryGet($"{component.Id.Value}.fired", out var fired) &&
                fired.Kind == StateValueKind.Bool && fired.AsBool())
                removed.Add(component.Id.Value);
        }
        var draws = new List<KnownDraw>();
        foreach (var draw in _burns.KnownDraws)
        {
            if (tanks.Contains(draw.TankId)) draws.Add(draw);
        }
        return new KnownMassEvents(removed, draws);
    }

    private static GroundMassLedgerSpec SpecOf(VehicleDefinition vehicle, FactCatalog facts, double dryTolerance)
    {
        var dry = new List<LedgerDryMass>();
        var tanks = new List<LedgerTank>();
        foreach (var section in vehicle.Sections) dry.Add(new LedgerDryMass(section.Id, section.Id, section.DryMassKg));
        foreach (var component in vehicle.Components)
        {
            var id = component.Id.Value;
            if (component.Payload is { } payload) dry.Add(new LedgerDryMass(id, component.Section, payload.MassKg));
            if (component.RetroRocket is { } retro) dry.Add(new LedgerDryMass(id, component.Section, retro.PropellantKg));
            if (component.Tank is not { } tank) continue;
            if (PropellantFact(facts, component.Id) is not { } fact)
            {
                dry.Add(new LedgerDryMass(id, component.Section, tank.InitialKg));
                continue;
            }
            var gauge = component.Sensors.Count > 0 ? vehicle.Components.FirstOrDefault(c => c.Id == component.Sensors[0] && c.Sensor is not null) : null;
            var toleranceKg = gauge?.Sensor is { } sensor ? Math.Max(sensor.Quantization, 2 * sensor.NoiseStd) : 0;
            tanks.Add(new LedgerTank(id, component.Section, fact, tank.CapacityKg, toleranceKg, gauge?.Id));
        }
        return new GroundMassLedgerSpec(vehicle.Id, dry, tanks, dryTolerance);
    }

    /// <summary>The number fact whose source is the tank (its gauge reading); null when none.</summary>
    private static FactId? PropellantFact(FactCatalog facts, ComponentId tank)
    {
        foreach (var fact in facts.Facts)
        {
            if (fact.Kind == FactValueKind.Number && fact.SourceComponent == tank && fact.ConfirmsComponent is null) return fact.Id;
        }
        return null;
    }
}
