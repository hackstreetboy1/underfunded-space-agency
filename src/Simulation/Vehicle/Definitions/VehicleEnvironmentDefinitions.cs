using Usa.Simulation.Kernel;

namespace Usa.Simulation.Vehicle.Definitions;

/// <summary>Heat conduction between two thermal-node components.</summary>
public sealed record ThermalLinkDefinition(ComponentId A, ComponentId B, double ConductanceWPerK);

/// <summary>Cabin atmosphere parameters consumed by the life-support models.</summary>
public sealed record CabinDefinition
{
    /// <summary>The compartment component that is the cabin.</summary>
    public required ComponentId Compartment { get; init; }

    /// <summary>The thermal node holding the cabin air temperature.</summary>
    public required ComponentId ThermalNode { get; init; }

    public required double VolumeM3 { get; init; }

    public required double InitialPressureKpa { get; init; }

    public required double InitialPpO2Kpa { get; init; }

    public required double InitialPpCo2Kpa { get; init; }

    public required double LeakRateKgPerS { get; init; }
}

/// <summary>Food and drinking water carried aboard (fuel-cell water is tracked in a tank component).</summary>
public sealed record ProvisionsDefinition(double FoodKcal, double PotableWaterL);
