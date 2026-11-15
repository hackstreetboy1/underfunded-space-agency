using Usa.Simulation.Kernel;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Presentation.Diagram;

/// <summary>A directed link between two components (by id), before placement filtering.</summary>
internal readonly record struct Edge(string From, string To, ConnectionKind Kind)
{
    public string Key => $"{KindName(Kind)}:{From}->{To}";

    private static string KindName(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Power => "power",
        ConnectionKind.Fluid => "fluid",
        ConnectionKind.Propellant => "propellant",
        _ => "data",
    };
}

/// <summary>Derives diagram connections from component specs. The vehicle files have no explicit wiring list.</summary>
internal static class ConnectionExtractor
{
    public static List<Edge> Extract(IReadOnlyDictionary<string, DiagramLayout.Member> members)
    {
        var edges = new List<Edge>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(ComponentId? from, ComponentId? to, ConnectionKind kind)
        {
            if (from is not { } source || to is not { } target || source == target) return;
            if (!members.ContainsKey(source.Value) || !members.ContainsKey(target.Value)) return;
            var edge = new Edge(source.Value, target.Value, kind);
            if (seen.Add(edge.Key)) edges.Add(edge);
        }

        foreach (var (_, member) in members.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var c = member.Definition;
            var id = c.Id;

            // Power flows source → bus → switch/breaker/harness → consumer.
            Add(c.Load?.Bus, id, ConnectionKind.Power);
            foreach (var spec in new[] { c.Switch, c.Breaker })
            {
                if (spec is null) continue;
                Add(spec.From, id, ConnectionKind.Power);
                Add(id, spec.To, ConnectionKind.Power);
            }
            if (c.Harness is { } harness)
            {
                Add(harness.From, id, ConnectionKind.Power);
                Add(id, harness.To, ConnectionKind.Power);
            }
            Add(c.Outlet?.Bus, id, ConnectionKind.Power);
            Add(id, c.FuelCell?.Bus, ConnectionKind.Power);
            Add(id, c.Battery?.Bus, ConnectionKind.Power);

            if (c.FuelCell is { } cell)
            {
                Add(cell.O2Tank, id, ConnectionKind.Fluid);
                Add(cell.H2Tank, id, ConnectionKind.Fluid);
                Add(id, cell.WaterTank, ConnectionKind.Fluid);
            }
            if (c.Regulator is { } regulator)
            {
                Add(regulator.Tank, id, ConnectionKind.Fluid);
                Add(regulator.SecondaryTank, id, ConnectionKind.Fluid);
            }
            if (c.Radiator is { } radiator) Add(radiator.Node, id, ConnectionKind.Fluid);

            foreach (var thruster in new[] { c.Thruster, c.Engine })
            {
                if (thruster is null) continue;
                Add(thruster.Tank, id, ConnectionKind.Propellant);
                Add(thruster.Driver, id, ConnectionKind.Data);
                Add(thruster.Group, id, ConnectionKind.Data);
            }

            foreach (var sensor in c.Sensors) Add(sensor, id, ConnectionKind.Data);
            if (c.Sensor is { } sensorSpec)
            {
                foreach (var channel in sensorSpec.Channels)
                {
                    var dot = channel.Measures.IndexOf('.', StringComparison.Ordinal);
                    if (dot > 0 && members.ContainsKey(channel.Measures[..dot])) Add(id, new ComponentId(channel.Measures[..dot]), ConnectionKind.Data);
                }
            }
        }
        return edges;
    }
}
