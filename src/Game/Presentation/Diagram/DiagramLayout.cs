using Usa.Simulation.Kernel;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Presentation.Diagram;

/// <summary>
/// The static structure of the internal 2D diagram (UI.md 4.5), built from vehicle definitions: compartments,
/// component nodes at their JSON <c>diagram</c> positions, and connections derived from the component specs
/// (bus feeds, switches, breakers and harnesses for power; reactant, oxygen, water and coolant links for fluids;
/// tank feeds for propellant; drivers, thruster groups and sensors for data). Immutable; per-frame state
/// (knowledge, layers, jettisoned sections, selection) lives in <see cref="DiagramScene"/>.
/// </summary>
public sealed class DiagramLayout
{
    private readonly Dictionary<string, DiagramNode> _nodeById;
    private readonly Dictionary<string, Member> _members;
    private readonly Dictionary<string, List<Link>> _powerUpstream;
    private readonly Dictionary<string, List<Link>> _powerDownstream;
    private readonly HashSet<string> _busTies;

    private DiagramLayout(
        DiagramLayoutOptions options, DiagramNode[] nodes, DiagramCompartment[] compartments, DiagramConnection[] connections,
        ComponentId[] unplaced, Dictionary<string, Member> members, List<Edge> powerEdges, Dictionary<string, string> connectionIds)
    {
        Options = options;
        Nodes = nodes;
        Compartments = compartments;
        Connections = connections;
        Unplaced = unplaced;
        _members = members;
        _nodeById = nodes.ToDictionary(n => n.Id.Value, StringComparer.Ordinal);
        _powerUpstream = new Dictionary<string, List<Link>>(StringComparer.Ordinal);
        _powerDownstream = new Dictionary<string, List<Link>>(StringComparer.Ordinal);
        _busTies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in powerEdges)
        {
            connectionIds.TryGetValue(edge.Key, out var connectionId);
            Add(_powerUpstream, edge.To, new Link(edge.From, connectionId));
            Add(_powerDownstream, edge.From, new Link(edge.To, connectionId));
        }
        foreach (var (id, member) in members)
        {
            if (member.Definition.Switch is { To: { } to } tie && IsFamily(tie.From.Value, ComponentFamily.Bus) && IsFamily(to.Value, ComponentFamily.Bus))
                _busTies.Add(id);
        }
        foreach (var links in _powerUpstream.Values) links.Sort((a, b) => string.CompareOrdinal(a.Component, b.Component));
        foreach (var links in _powerDownstream.Values) links.Sort((a, b) => string.CompareOrdinal(a.Component, b.Component));
    }

    public DiagramLayoutOptions Options { get; }

    /// <summary>Placed components, sorted by id.</summary>
    public IReadOnlyList<DiagramNode> Nodes { get; }

    /// <summary>Sorted by id.</summary>
    public IReadOnlyList<DiagramCompartment> Compartments { get; }

    /// <summary>Sorted by id.</summary>
    public IReadOnlyList<DiagramConnection> Connections { get; }

    /// <summary>Components without a <c>diagram</c> placement (still part of the power graph).</summary>
    public IReadOnlyList<ComponentId> Unplaced { get; }

    public static DiagramLayout Build(IReadOnlyList<VehicleDefinition> vehicles, DiagramLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        options ??= DiagramLayoutOptions.Default;
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (var vehicle in vehicles)
        {
            foreach (var component in vehicle.Components)
            {
                if (!members.TryAdd(component.Id.Value, new Member(vehicle.Id, component)))
                    throw new ArgumentException($"Component '{component.Id}' appears in more than one vehicle.", nameof(vehicles));
            }
        }

        var nodes = new List<DiagramNode>();
        var unplaced = new List<ComponentId>();
        foreach (var (_, member) in members.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var c = member.Definition;
            if (c.Diagram is not { } placement)
            {
                unplaced.Add(c.Id);
                continue;
            }
            nodes.Add(new DiagramNode(
                c.Id, member.VehicleId, c.Section, c.Name, c.Family, DiagramStyles.IconKey(c.Family),
                new DiagramPoint(placement.X, placement.Y), placement.Compartment, c.Control));
        }

        var edges = ConnectionExtractor.Extract(members);
        var placed = nodes.ToDictionary(n => n.Id.Value, StringComparer.Ordinal);
        var connections = new List<DiagramConnection>();
        var connectionIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!placed.TryGetValue(edge.From, out var from) || !placed.TryGetValue(edge.To, out var to)) continue;
            var id = edge.Key;
            connectionIds[id] = id;
            connections.Add(new DiagramConnection(id, from.Id, to.Id, edge.Kind, DiagramStyles.For(edge.Kind), Route(from.Position, to.Position)));
        }
        connections.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        var powerEdges = edges.Where(e => e.Kind == ConnectionKind.Power).ToList();
        return new DiagramLayout(
            options, [.. nodes], BuildCompartments(nodes, options), [.. connections], [.. unplaced], members, powerEdges, connectionIds);
    }

    public bool TryGetNode(ComponentId id, out DiagramNode node) => _nodeById.TryGetValue(id.Value, out node!);

    public SectionKey SectionOf(ComponentId id) =>
        _members.TryGetValue(id.Value, out var member)
            ? new SectionKey(member.VehicleId, member.Definition.Section)
            : throw new KeyNotFoundException($"Unknown component '{id}'.");

    public string NameOf(ComponentId id) => _members.TryGetValue(id.Value, out var member) ? member.Definition.Name : id.Value;

    /// <summary>
    /// Walks the power graph from <paramref name="load"/> back to its sources through harnesses, breakers, switches
    /// and buses. Bus ties are crossed in both directions when <paramref name="followBusTies"/> is set (a tie can
    /// feed either bus). Components for which <paramref name="isHidden"/> is true (jettisoned) are not crossed.
    /// </summary>
    public SupplyPath SupplyPathOf(ComponentId load, Func<ComponentId, bool>? isHidden = null, bool followBusTies = true)
    {
        var components = new List<ComponentId> { load };
        var connections = new List<string>();
        var sources = new List<ComponentId>();
        if (!_members.ContainsKey(load.Value) || isHidden?.Invoke(load) == true) return new SupplyPath(load, components, connections, sources);

        var visited = new HashSet<string>(StringComparer.Ordinal) { load.Value };
        var queue = new Queue<string>();
        queue.Enqueue(load.Value);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var link in UpstreamOf(current, followBusTies))
            {
                var id = _members[link.Component].Definition.Id;
                if (isHidden?.Invoke(id) == true) continue;
                if (link.ConnectionId is { } connectionId && !connections.Contains(connectionId)) connections.Add(connectionId);
                if (!visited.Add(link.Component)) continue;
                components.Add(id);
                if (IsSource(link.Component)) sources.Add(id);
                queue.Enqueue(link.Component);
            }
        }
        return new SupplyPath(load, components, connections, sources);
    }

    /// <summary>The consumers <paramref name="component"/> powers, through switches, breakers, harnesses, buses and outlets.</summary>
    public IReadOnlyList<ComponentId> FeedsOf(ComponentId component, Func<ComponentId, bool>? isHidden = null)
    {
        var loads = new List<ComponentId>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { component.Value };
        var queue = new Queue<string>();
        queue.Enqueue(component.Value);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_powerDownstream.TryGetValue(current, out var links)) continue;
            foreach (var link in links)
            {
                if (!visited.Add(link.Component)) continue;
                var id = _members[link.Component].Definition.Id;
                if (isHidden?.Invoke(id) == true) continue;
                if (IsConductor(link.Component)) queue.Enqueue(link.Component);
                else loads.Add(id);
            }
        }
        loads.Sort();
        return loads;
    }

    /// <summary>The buses that can feed <paramref name="component"/> (nearest first).</summary>
    public IReadOnlyList<ComponentId> BusesFeeding(ComponentId component, Func<ComponentId, bool>? isHidden = null)
    {
        var path = SupplyPathOf(component, isHidden, followBusTies: false);
        var buses = new List<ComponentId>();
        foreach (var id in path.Components)
        {
            if (id != component && IsFamily(id.Value, ComponentFamily.Bus)) buses.Add(id);
        }
        return buses;
    }

    private IEnumerable<Link> UpstreamOf(string component, bool followBusTies)
    {
        if (_powerUpstream.TryGetValue(component, out var links))
        {
            foreach (var link in links) yield return link;
        }
        if (!followBusTies || !_powerDownstream.TryGetValue(component, out var downstream)) yield break;
        // Structural only: switch state is not read, so with ties followed a tie counts as closed and the walk may
        // continue from its "from" bus through it.
        foreach (var link in downstream)
        {
            if (_busTies.Contains(link.Component)) yield return link;
        }
        if (_busTies.Contains(component))
        {
            foreach (var link in downstream) yield return link;
        }
    }

    private bool IsFamily(string id, ComponentFamily family) => _members.TryGetValue(id, out var m) && m.Definition.Family == family;

    private bool IsSource(string id) => _members[id].Definition.Family is ComponentFamily.FuelCell or ComponentFamily.Battery;

    private bool IsConductor(string id) =>
        _members[id].Definition.Family is ComponentFamily.Switch or ComponentFamily.Breaker or ComponentFamily.Harness
            or ComponentFamily.Bus or ComponentFamily.Outlet;

    private static List<DiagramPoint> Route(DiagramPoint from, DiagramPoint to) =>
        from.X == to.X || from.Y == to.Y ? [from, to] : [from, new DiagramPoint(to.X, from.Y), to];

    private static DiagramCompartment[] BuildCompartments(List<DiagramNode> nodes, DiagramLayoutOptions options)
    {
        var overrides = new Dictionary<string, CompartmentLayout>(StringComparer.Ordinal);
        foreach (var layout in options.Compartments ?? []) overrides[layout.Id] = layout;

        return nodes
            .GroupBy(n => n.CompartmentId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                overrides.TryGetValue(group.Key, out var explicitLayout);
                var bounds = explicitLayout?.Bounds ?? DiagramRect.Around(group.Select(n => n.Position), options.CompartmentPaddingPx);
                var sections = group
                    .Select(n => new SectionKey(n.VehicleId, n.SectionId))
                    .Distinct()
                    .OrderBy(s => s.VehicleId, StringComparer.Ordinal)
                    .ThenBy(s => s.SectionId, StringComparer.Ordinal)
                    .ToArray();
                return new DiagramCompartment(group.Key, explicitLayout?.Label ?? DefaultLabel(group.Key), bounds, sections);
            })
            .ToArray();
    }

    /// <summary><c>adapter_fwd</c> → <c>Adapter fwd</c>.</summary>
    private static string DefaultLabel(string compartmentId)
    {
        if (compartmentId.Length == 0) return compartmentId;
        var text = compartmentId.Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static void Add(Dictionary<string, List<Link>> map, string key, Link link)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(link);
    }

    internal readonly record struct Member(string VehicleId, ComponentDefinition Definition);

    private readonly record struct Link(string Component, string? ConnectionId);
}
