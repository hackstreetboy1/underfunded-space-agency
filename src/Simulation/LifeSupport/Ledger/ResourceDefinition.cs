namespace Usa.Simulation.LifeSupport.Ledger;

/// <summary>A resource tracked by the <see cref="ResourceLedger"/>, e.g. <c>o2_kg</c>, <c>water_l</c>, <c>battery_kwh</c>.</summary>
public sealed record ResourceDefinition(string Id, string Unit, double Capacity, double InitialQuantity);

/// <summary>Current figures of one ledger resource.</summary>
/// <param name="TrendPerH">Least-squares slope over the sampled history; the instantaneous net rate until two samples exist.</param>
public readonly record struct ResourceSnapshot(
    string Id,
    string Unit,
    double Quantity,
    double Capacity,
    double ProductionPerS,
    double ConsumptionPerS,
    double TrendPerH,
    int HistoryCount)
{
    public double NetPerS => ProductionPerS - ConsumptionPerS;

    public double FractionOfCapacity => Capacity > 0 ? Quantity / Capacity : 0;
}
