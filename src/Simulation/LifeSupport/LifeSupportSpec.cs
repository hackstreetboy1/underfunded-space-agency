using Usa.Simulation.LifeSupport.Atmosphere;
using Usa.Simulation.LifeSupport.Consumables;
using Usa.Simulation.LifeSupport.Thermal;

namespace Usa.Simulation.LifeSupport;

/// <summary>Everything vehicle-specific life support needs; integration builds it from the vehicle files.</summary>
public sealed record LifeSupportSpec(ThermalNetworkSpec Thermal, IReadOnlyList<CabinSpec> Cabins, ProvisionsSpec Provisions);
