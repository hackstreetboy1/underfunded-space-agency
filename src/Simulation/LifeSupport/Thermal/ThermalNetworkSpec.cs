using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.LifeSupport.Config;

namespace Usa.Simulation.LifeSupport.Thermal;

/// <summary>A lumped thermal mass, filled by integration from a vehicle's <c>thermal_node</c> component.</summary>
/// <param name="SafeMinC">Lower edge of the safe band; leaving the band is the warning used for time-to-warning.</param>
/// <param name="SolarAbsorbedPeakW">
/// Solar heat absorbed in full sun; scaled by <see cref="ThermalInputs.SunlitFraction"/>, the node's
/// <see cref="ThermalInputs.SolarExposure"/> and its persistent solar factor.
/// </param>
/// <param name="Solar">Attitude dependence of the solar heating, applied by <see cref="SolarHeating.Fill"/>; null keeps it constant.</param>
public sealed record ThermalNodeSpec(
    ComponentId Id,
    double HeatCapacityJPerK,
    double InitialTempC,
    double SafeMinC,
    double SafeMaxC,
    double SolarAbsorbedPeakW = 0,
    NodeSolarSpec? Solar = null);

/// <summary>Conduction (or a pumped coolant loop modelled as conduction) between two nodes.</summary>
public sealed record ThermalLinkSpec(ComponentId NodeA, ComponentId NodeB, double ConductanceWPerK);

/// <summary>A radiator rejecting heat from <paramref name="Node"/> as εσA(T⁴ − T_env⁴).</summary>
public sealed record RadiatorSpec(ComponentId Id, ComponentId Node, double Emissivity, double AreaM2);

public sealed record ThermalNetworkSpec(
    IReadOnlyList<ThermalNodeSpec> Nodes,
    IReadOnlyList<ThermalLinkSpec> Links,
    IReadOnlyList<RadiatorSpec> Radiators)
{
    /// <summary>Reports every problem; <paramref name="file"/> labels the vehicle file the spec came from.</summary>
    public void Validate(DefinitionErrors errors, string file)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var check = new DataChecks(errors, file);
        var nodeIds = new HashSet<ComponentId>();
        for (var i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            var path = $"$.thermal.nodes[{i}]";
            if (!nodeIds.Add(node.Id)) errors.Add(file, path + ".id", $"Thermal node '{node.Id}' is defined twice.");
            check.Positive(path + ".heatCapacityJPerK", node.HeatCapacityJPerK);
            check.NotNegative(path + ".solarAbsorbedPeakW", node.SolarAbsorbedPeakW);
            if (!(node.SafeMinC < node.SafeMaxC)) errors.Add(file, path, "safeMinC must be below safeMaxC.");
            if (node.InitialTempC + PhysicalConstants.ZeroCelsiusInKelvin <= 0) errors.Add(file, path + ".initialTempC", "Below absolute zero.");
            node.Solar?.Validate(errors, file, path + ".solar");
            if (node.Solar?.Surfaces is { Count: > 0 } && node.SolarAbsorbedPeakW > 0)
                errors.Add(file, path + ".solarAbsorbedPeakW", "A node heated by solar surfaces has no peak as well (that would count the same sunlight twice).");
        }

        for (var i = 0; i < Links.Count; i++)
        {
            var link = Links[i];
            var path = $"$.thermal.links[{i}]";
            if (!nodeIds.Contains(link.NodeA)) errors.Add(file, path + ".nodeA", $"Unknown thermal node '{link.NodeA}'.");
            if (!nodeIds.Contains(link.NodeB)) errors.Add(file, path + ".nodeB", $"Unknown thermal node '{link.NodeB}'.");
            if (link.NodeA == link.NodeB) errors.Add(file, path, "A link must join two different nodes.");
            check.NotNegative(path + ".conductanceWPerK", link.ConductanceWPerK);
        }

        var radiatorIds = new HashSet<ComponentId>();
        for (var i = 0; i < Radiators.Count; i++)
        {
            var radiator = Radiators[i];
            var path = $"$.thermal.radiators[{i}]";
            if (!radiatorIds.Add(radiator.Id)) errors.Add(file, path + ".id", $"Radiator '{radiator.Id}' is defined twice.");
            if (nodeIds.Contains(radiator.Id)) errors.Add(file, path + ".id", $"Radiator '{radiator.Id}' reuses a thermal node id.");
            if (!nodeIds.Contains(radiator.Node)) errors.Add(file, path + ".node", $"Unknown thermal node '{radiator.Node}'.");
            check.InRange(path + ".emissivity", radiator.Emissivity, 1e-6, 1);
            check.Positive(path + ".areaM2", radiator.AreaM2);
        }
    }
}
