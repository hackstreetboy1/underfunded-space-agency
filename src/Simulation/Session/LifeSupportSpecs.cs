using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.LifeSupport;
using Usa.Simulation.LifeSupport.Atmosphere;
using Usa.Simulation.LifeSupport.Consumables;
using Usa.Simulation.LifeSupport.Thermal;
using Usa.Simulation.Vehicle.Definitions;
using ThermalNode = Usa.Simulation.LifeSupport.Thermal.ThermalNodeSpec;
using ThermalRadiator = Usa.Simulation.LifeSupport.Thermal.RadiatorSpec;

namespace Usa.Simulation.Session;

/// <summary>Builds the life-support spec from the vehicle files (thermal nodes, radiators, cabins, provisions).</summary>
internal static class LifeSupportSpecs
{
    private const string WaterResource = "water";

    /// <exception cref="DefinitionException">A cabin has no scrubber, or no vehicle carries provisions.</exception>
    public static LifeSupportSpec Create(IReadOnlyList<VehicleDefinition> vehicles)
    {
        var errors = new DefinitionErrors();
        var nodes = new List<ThermalNode>();
        var links = new List<ThermalLinkSpec>();
        var radiators = new List<ThermalRadiator>();
        var cabins = new List<CabinSpec>();
        ProvisionsSpec? provisions = null;

        foreach (var vehicle in vehicles.OrderBy(v => v.Id, StringComparer.Ordinal))
        {
            var file = $"vehicles/{vehicle.Id}.json";
            foreach (var component in vehicle.Components)
            {
                if (component.ThermalNode is { } node)
                {
                    var solar = SolarOf(node.Solar, file, $"$.components[{component.Id}].thermalNode", errors);
                    if (solar?.Surfaces is { Count: > 0 } && node.SolarAbsorbedPeakW > 0)
                        errors.Add(file, $"$.components[{component.Id}].thermalNode.solarAbsorbedPeakW",
                            "A node heated by solar surfaces has no peak as well (that would count the same sunlight twice).");
                    nodes.Add(new ThermalNode(component.Id, node.HeatCapacityJPerK, node.InitialC, node.MinC, node.MaxC, node.SolarAbsorbedPeakW, solar));
                }
                if (component.Radiator is { } radiator)
                    radiators.Add(new ThermalRadiator(component.Id, radiator.Node, radiator.Emissivity, radiator.AreaM2));
            }
            foreach (var link in vehicle.ThermalLinks) links.Add(new ThermalLinkSpec(link.A, link.B, link.ConductanceWPerK));

            if (vehicle.Cabin is { } cabin) cabins.Add(CabinOf(vehicle, cabin, file, errors));
            if (vehicle.Provisions is { } stowed && provisions is null)
            {
                var tankL = vehicle.Components.Where(c => c.Tank is { Resource: WaterResource }).Sum(c => c.Tank!.CapacityKg);
                provisions = new ProvisionsSpec(stowed.FoodKcal, stowed.PotableWaterL, stowed.PotableWaterL + tankL);
            }
        }

        if (provisions is null) errors.Add("vehicles", "$.provisions", "No vehicle carries provisions.");
        errors.ThrowIfAny();
        return new LifeSupportSpec(new ThermalNetworkSpec(nodes, links, radiators), cabins, provisions!);
    }

    /// <summary>True when any thermal node of these vehicles takes solar heat (a peak or a solar block).</summary>
    public static bool HasSolarHeating(IReadOnlyList<VehicleDefinition> vehicles) =>
        vehicles.Any(v => v.Components.Any(c => c.ThermalNode is { } node && (node.SolarAbsorbedPeakW > 0 || node.Solar is not null)));

    private static NodeSolarSpec? SolarOf(ThermalSolarSpec? solar, string file, string path, DefinitionErrors errors)
    {
        if (solar is null) return null;
        SolarCurvePoint[]? curve = null;
        if (solar.PitchCurve is { } points)
        {
            curve = new SolarCurvePoint[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i].Count != 2) errors.Add(file, $"{path}.solar.pitchCurve[{i}]", "Each point is [sunPitchDeg, factor].");
                else curve[i] = new SolarCurvePoint(points[i][0], points[i][1]);
            }
        }
        SolarSurfaceSpec[]? surfaces = null;
        if (solar.Surfaces is { } list)
        {
            surfaces = new SolarSurfaceSpec[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                var normal = list[i].Normal;
                if (normal.Count != 3) errors.Add(file, $"{path}.solar.surfaces[{i}].normal", "The normal is [x, y, z].");
                surfaces[i] = new SolarSurfaceSpec(normal.Count == 3 ? new Vec3d(normal[0], normal[1], normal[2]) : Vec3d.Zero, list[i].AreaM2, list[i].Absorptivity);
            }
        }
        var spec = new NodeSolarSpec(curve, surfaces);
        spec.Validate(errors, file, path + ".solar");
        return spec;
    }

    private static CabinSpec CabinOf(VehicleDefinition vehicle, CabinDefinition cabin, string file, DefinitionErrors errors)
    {
        var lioh = vehicle.Components.Where(c => c.Scrubber is not null).Sum(c => c.Scrubber!.CapacityKgCo2);
        if (lioh <= 0) errors.Add(file, "$.components", "The cabin needs a scrubber component with capacityKgCo2.");
        var fans = vehicle.Components.Where(c => c.Fan is not null).Sum(c => c.Fan!.AirflowM3PerS);
        var regulator = vehicle.Components.FirstOrDefault(c => c.Regulator is not null)?.Regulator;
        var scrubber = vehicle.Components.FirstOrDefault(c => c.Scrubber is not null)?.Scrubber;

        var inertKpa = Math.Max(0, cabin.InitialPressureKpa - cabin.InitialPpO2Kpa - cabin.InitialPpCo2Kpa);
        var tempC = vehicle.Components.FirstOrDefault(c => c.Id == cabin.ThermalNode)?.ThermalNode?.InitialC ?? 20;
        var gasKg = MassKg(cabin.InitialPpO2Kpa, GasConstants.MolarMassO2KgPerMol, cabin.VolumeM3, tempC)
                    + MassKg(cabin.InitialPpCo2Kpa, GasConstants.MolarMassCo2KgPerMol, cabin.VolumeM3, tempC)
                    + MassKg(inertKpa, GasConstants.MolarMassInertKgPerMol, cabin.VolumeM3, tempC);
        var leakPctPerHour = gasKg > 0 ? cabin.LeakRateKgPerS * GasConstants.SecondsPerHour / gasKg * 100.0 : 0;

        return new CabinSpec(
            vehicle.Id,
            cabin.VolumeM3,
            cabin.ThermalNode,
            KpaToMmhg(cabin.InitialPpO2Kpa),
            KpaToMmhg(cabin.InitialPpCo2Kpa),
            KpaToMmhg(inertKpa),
            leakPctPerHour,
            Math.Max(lioh, double.Epsilon),
            fans,
            regulator is null ? null : KpaToMmhg(regulator.SetpointKpa),
            regulator is null ? null : regulator.MaxFlowKgPerS * GasConstants.SecondsPerHour,
            scrubber?.RemovalEfficiency);
    }

    public static double KpaToMmhg(double kpa) => kpa * 1000.0 / GasConstants.PaPerMmhg;

    private static double MassKg(double partialKpa, double molarMassKgPerMol, double volumeM3, double tempC) =>
        partialKpa * 1000.0 * volumeM3 * molarMassKgPerMol / (GasConstants.UniversalGasConstantJPerMolK * (tempC + PhysicalConstants.ZeroCelsiusInKelvin));
}
