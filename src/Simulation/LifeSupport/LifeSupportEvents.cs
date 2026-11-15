using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Thermal;

namespace Usa.Simulation.LifeSupport;

/// <summary>A thermal node entered or left its safe range.</summary>
public sealed record ThermalBandChanged(MissionTime Time, ComponentId NodeId, ThermalBand Band, double TempC) : ISimEvent;

/// <summary>The cabin O2 regulator started or stopped being starved by the supply.</summary>
public sealed record CabinO2SupplyChanged(MissionTime Time, string CabinId, bool IsShort) : ISimEvent;

/// <summary>The LiOH canister passed breakthrough: CO2 removal is now falling with remaining capacity.</summary>
public sealed record LiOhBreakthroughReached(MissionTime Time, string CabinId, double LiOhPct) : ISimEvent;

public sealed record LiOhCanisterReplaced(MissionTime Time, string CabinId) : ISimEvent;

/// <param name="ResourceId"><c>provisions.food_kcal</c> or <c>provisions.water_l</c>.</param>
public sealed record ProvisionExhausted(MissionTime Time, string ResourceId) : ISimEvent;
