using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Config;

namespace Usa.Simulation.LifeSupport.Atmosphere;

/// <summary>A pressurised cabin, filled by integration from the vehicle's cabin compartment and LiOH canister.</summary>
/// <param name="Id">The vehicle id (e.g. <c>tandem</c>); state paths are <c>cabin.&lt;id&gt;.&lt;field&gt;</c>.</param>
/// <param name="ThermalNode">Node whose temperature is the cabin air temperature and which receives crew heat.</param>
/// <param name="InitialInertMmhg">Diluent (nitrogen) partial pressure; 0 for a pure-oxygen cabin.</param>
/// <param name="LeakPctPerHour">Fraction of the cabin gas lost per hour, all species alike (well mixed).</param>
/// <param name="LiOhCapacityKgCo2">CO2 a fresh canister can absorb.</param>
/// <param name="FanAirflowM3PerS">Air pushed through the canister with the fan at full speed.</param>
/// <param name="PpO2SetpointMmhg">This vehicle's regulator setpoint; null uses the global <see cref="RegulatorConfig"/>.</param>
/// <param name="MaxO2FlowKgPerHour">This vehicle's regulator maximum flow; null uses the global <see cref="RegulatorConfig"/>.</param>
/// <param name="ScrubberEfficiency">This vehicle's LiOH removal efficiency (0-1); null uses the global <see cref="ScrubberConfig"/>.</param>
public sealed record CabinSpec(
    string Id,
    double VolumeM3,
    ComponentId ThermalNode,
    double InitialPpO2Mmhg,
    double InitialPpCo2Mmhg,
    double InitialInertMmhg,
    double LeakPctPerHour,
    double LiOhCapacityKgCo2,
    double FanAirflowM3PerS,
    double? PpO2SetpointMmhg = null,
    double? MaxO2FlowKgPerHour = null,
    double? ScrubberEfficiency = null)
{
    public void Validate(DefinitionErrors errors, string file)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var check = new DataChecks(errors, file);
        if (string.IsNullOrEmpty(Id) || Id.Contains('.', StringComparison.Ordinal))
            errors.Add(file, "$.cabin.id", $"Cabin id '{Id}' must be a non-empty vehicle id without '.'.");
        check.Positive("$.cabin.volumeM3", VolumeM3);
        check.NotNegative("$.cabin.initialPpO2Mmhg", InitialPpO2Mmhg);
        check.NotNegative("$.cabin.initialPpCo2Mmhg", InitialPpCo2Mmhg);
        check.NotNegative("$.cabin.initialInertMmhg", InitialInertMmhg);
        check.NotNegative("$.cabin.leakPctPerHour", LeakPctPerHour);
        check.Positive("$.cabin.liOhCapacityKgCo2", LiOhCapacityKgCo2);
        check.NotNegative("$.cabin.fanAirflowM3PerS", FanAirflowM3PerS);
        if (PpO2SetpointMmhg is { } setpoint) check.Positive("$.cabin.ppO2SetpointMmhg", setpoint);
        if (MaxO2FlowKgPerHour is { } maxFlow) check.Positive("$.cabin.maxO2FlowKgPerHour", maxFlow);
        if (ScrubberEfficiency is { } efficiency) check.InRange("$.cabin.scrubberEfficiency", efficiency, 0, 1);
    }
}
