using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge.Ledger;

/// <summary>
/// What the ground has on paper about one vehicle: design masses of every section and fixed load, and its propellant
/// tanks. Built from vehicle data by the integrator (sections' <c>dryMassKg</c>, unburnt retro propellant, tanks).
/// </summary>
/// <param name="DryTolerance">Fractional uncertainty of the design masses (0.003 = ±0.3 %, scaled by difficulty).</param>
public sealed record GroundMassLedgerSpec(
    string VehicleId,
    IReadOnlyList<LedgerDryMass> DryMasses,
    IReadOnlyList<LedgerTank> Tanks,
    double DryTolerance = GroundMassLedgerSpec.DefaultDryTolerance)
{
    public const double DefaultDryTolerance = 0.003;
}

/// <summary>A design mass: a section's structure (<paramref name="Id"/> equals <paramref name="Section"/>) or a fixed load such as a retro rocket's propellant.</summary>
/// <param name="Section">The section it rides on; jettisoning the section removes it.</param>
public sealed record LedgerDryMass(string Id, string Section, double DesignMassKg);

/// <summary>A propellant tank and how the ground learns its contents.</summary>
/// <param name="PropellantFact">The number fact carrying gauge readings and, when it has a downlink, ground bands.</param>
/// <param name="ReadingToleranceKg">Half-width of a gauge reading's band (quantization plus noise), e.g. 5 kg.</param>
/// <param name="Gauge">The gauge sensor component, whose confirmed or suspected bias the ledger applies; null when it has none.</param>
/// <param name="GaugeBiasKg">Authored bias magnitude of that gauge's fault variant (how far it can read off).</param>
public sealed record LedgerTank(
    string Id,
    string Section,
    FactId PropellantFact,
    double CapacityKg,
    double ReadingToleranceKg,
    ComponentId? Gauge = null,
    double GaugeBiasKg = 0);

/// <summary>Mass changes the ground knows were commanded: jettisons, spent loads and engine-spec propellant draws.</summary>
/// <param name="Removed">Jettisoned section ids (removes everything on the section) and spent dry-mass ids.</param>
/// <param name="Draws">Propellant the ground knows was used, applied to every observation made before the draw.</param>
public sealed record KnownMassEvents(IReadOnlySet<string> Removed, IReadOnlyList<KnownDraw> Draws)
{
    public static KnownMassEvents None { get; } = new(new HashSet<string>(StringComparer.Ordinal), []);
}

/// <summary>Propellant drawn from <paramref name="TankId"/> by a commanded burn, from the engine spec (not measured).</summary>
public readonly record struct KnownDraw(string TankId, MissionTime Time, double MassKg);
