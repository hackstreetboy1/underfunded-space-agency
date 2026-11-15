using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <summary>
/// The engineering workspace board for one repair goal: placed parts and the links between their connectors.
/// Each connector takes at most one link. Mismatched connectors may be linked; <see cref="AssemblyValidator"/>
/// explains why that fails, so the player learns from the digital test rather than from a refused drag.
/// </summary>
public sealed class AssemblyGraph : IHashContributor
{
    private readonly PartCatalog _catalog;
    private readonly List<PlacedPart> _parts = [];
    private readonly Dictionary<PortRef, PortRef> _links = [];
    private int _nextInstance = 1;

    public AssemblyGraph(PartCatalog catalog, RepairGoal goal, RepairSink sink)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(sink);
        if (!goal.SinkCandidates.Contains(sink)) throw new ArgumentException($"Sink is not a candidate of goal '{goal.Id}'.", nameof(sink));
        _catalog = catalog;
        Goal = goal;
        Sink = sink;
    }

    public RepairGoal Goal { get; }

    public RepairSink Sink { get; }

    /// <summary>Placed parts in instance order.</summary>
    public IReadOnlyList<PlacedPart> Parts => _parts;

    public AssemblyResult Place(PartId partId) => Place(partId, _nextInstance);

    private AssemblyResult Place(PartId partId, int instanceId)
    {
        if (!_catalog.TryGet(partId, out var part)) return new AssemblyResult(AssemblyFailure.UnknownPart);
        if (!part.IsAssemblyPart) return new AssemblyResult(AssemblyFailure.NotAnAssemblyPart);
        var instance = new PartInstanceId(instanceId);
        if (instanceId < 1 || IndexOf(instance) >= 0) return new AssemblyResult(AssemblyFailure.UnknownInstance, instance);
        _parts.Add(new PlacedPart(instance, part));
        _nextInstance = Math.Max(_nextInstance, instanceId + 1);
        return AssemblyResult.Ok(instance);
    }

    /// <summary>Removes a placed part and every link to it.</summary>
    public AssemblyResult Remove(PartInstanceId instance)
    {
        if (instance.IsEndpoint) return new AssemblyResult(AssemblyFailure.EndpointFixed, instance);
        var index = IndexOf(instance);
        if (index < 0) return new AssemblyResult(AssemblyFailure.UnknownInstance, instance);
        for (var port = 0; port < _parts[index].Part.Connectors.Count; port++) Unlink(new PortRef(instance, port));
        _parts.RemoveAt(index);
        return AssemblyResult.Ok(instance);
    }

    public AssemblyResult Connect(PortRef a, PortRef b)
    {
        var check = CheckPort(a);
        if (check != AssemblyFailure.None) return new AssemblyResult(check, a.Instance);
        check = CheckPort(b);
        if (check != AssemblyFailure.None) return new AssemblyResult(check, b.Instance);
        if (a.Instance == b.Instance) return new AssemblyResult(AssemblyFailure.SamePart, a.Instance);
        if (_links.ContainsKey(a)) return new AssemblyResult(AssemblyFailure.PortInUse, a.Instance);
        if (_links.ContainsKey(b)) return new AssemblyResult(AssemblyFailure.PortInUse, b.Instance);
        _links.Add(a, b);
        _links.Add(b, a);
        return AssemblyResult.Ok();
    }

    public AssemblyResult Disconnect(PortRef port)
    {
        var check = CheckPort(port);
        if (check != AssemblyFailure.None) return new AssemblyResult(check, port.Instance);
        return Unlink(port) ? AssemblyResult.Ok() : new AssemblyResult(AssemblyFailure.NotConnected, port.Instance);
    }

    public PortRef? LinkedTo(PortRef port) => _links.TryGetValue(port, out var other) ? other : null;

    public bool TryGetPart(PartInstanceId instance, out PlacedPart part)
    {
        var index = IndexOf(instance);
        part = index >= 0 ? _parts[index] : null!;
        return index >= 0;
    }

    /// <summary>Connector type id at <paramref name="port"/>.</summary>
    public string ConnectorAt(PortRef port)
    {
        if (port.Instance == PartInstanceId.Source) return Goal.Source.Connector;
        if (port.Instance == PartInstanceId.Sink) return Sink.Port.Connector;
        return TryGetPart(port.Instance, out var placed) && port.Port >= 0 && port.Port < placed.Part.Connectors.Count
            ? placed.Part.Connectors[port.Port]
            : throw new ArgumentOutOfRangeException(nameof(port), port, "No such port on the board.");
    }

    /// <summary>Player-facing name of a placed part or fixed port.</summary>
    public string LabelOf(PartInstanceId instance)
    {
        if (instance == PartInstanceId.Source) return Goal.Source.Label;
        if (instance == PartInstanceId.Sink) return Sink.Port.Label;
        return TryGetPart(instance, out var placed) ? placed.Part.Name : instance.ToString();
    }

    /// <summary>Every link once, lower port first, sorted.</summary>
    public IReadOnlyList<AssemblyLink> Links()
    {
        var links = new List<AssemblyLink>(_links.Count / 2);
        foreach (var (a, b) in _links)
        {
            if (Compare(a, b) < 0) links.Add(new AssemblyLink(a, b));
        }
        links.Sort((x, y) => Compare(x.A, y.A));
        return links;
    }

    /// <summary>The canonical design: parts renumbered 1..n in placement order. Commands and repair tasks use this form.</summary>
    public AssemblyDesign ToDesign()
    {
        var renumber = new Dictionary<PartInstanceId, PartInstanceId>(_parts.Count);
        var parts = new PartId[_parts.Count];
        for (var i = 0; i < _parts.Count; i++)
        {
            renumber.Add(_parts[i].Instance, new PartInstanceId(i + 1));
            parts[i] = _parts[i].Part.Id;
        }

        PortRef Map(PortRef port) => port.Instance.IsEndpoint ? port : port with { Instance = renumber[port.Instance] };

        var links = Links();
        var mapped = new AssemblyLink[links.Count];
        for (var i = 0; i < links.Count; i++) mapped[i] = new AssemblyLink(Map(links[i].A), Map(links[i].B));
        return new AssemblyDesign(Goal.Id, Sink.Definition.Component, parts, mapped);
    }

    /// <summary>The board as it stands, keeping live instance ids and the next id, for save and resume.</summary>
    public AssemblyDesign ToSavedDesign()
    {
        var parts = new PartId[_parts.Count];
        var instances = new int[_parts.Count];
        for (var i = 0; i < _parts.Count; i++)
        {
            parts[i] = _parts[i].Part.Id;
            instances[i] = _parts[i].Instance.Value;
        }
        return new AssemblyDesign(Goal.Id, Sink.Definition.Component, parts, Links(), instances, _nextInstance);
    }

    /// <summary>Rebuilds a board from a design; fails on the first part or link that doesn't apply.</summary>
    public static AssemblyResult TryRestore(PartCatalog catalog, RepairGoal goal, AssemblyDesign design, out AssemblyGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(design);
        graph = null;
        RepairSink? sink = null;
        foreach (var candidate in goal.SinkCandidates)
        {
            if (candidate.Definition.Component == design.Sink) sink = candidate;
        }
        if (sink is null || design.GoalId != goal.Id) return new AssemblyResult(AssemblyFailure.UnknownSink);

        if (design.Instances is { } ids && ids.Count != design.Parts.Count) return new AssemblyResult(AssemblyFailure.UnknownInstance);
        var board = new AssemblyGraph(catalog, goal, sink);
        for (var i = 0; i < design.Parts.Count; i++)
        {
            var placed = board.Place(design.Parts[i], design.Instances?[i] ?? i + 1);
            if (!placed.Succeeded) return placed;
        }
        board._nextInstance = Math.Max(board._nextInstance, design.NextInstance);
        foreach (var link in design.Links)
        {
            var connected = board.Connect(link.A, link.B);
            if (!connected.Succeeded) return connected;
        }
        graph = board;
        return AssemblyResult.Ok();
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Goal.Id);
        writer.Write(Sink.Definition.Component.Value);
        writer.Write((long)_nextInstance);
        writer.Write((long)_parts.Count);
        foreach (var placed in _parts)
        {
            writer.Write((long)placed.Instance.Value);
            writer.Write(placed.Part.Id.Value);
        }
        foreach (var link in Links())
        {
            writer.Write((long)link.A.Instance.Value);
            writer.Write((long)link.A.Port);
            writer.Write((long)link.B.Instance.Value);
            writer.Write((long)link.B.Port);
        }
    }

    private bool Unlink(PortRef port)
    {
        if (!_links.Remove(port, out var other)) return false;
        _links.Remove(other);
        return true;
    }

    private AssemblyFailure CheckPort(PortRef port)
    {
        if (port.Instance.IsEndpoint)
        {
            if (port.Instance != PartInstanceId.Source && port.Instance != PartInstanceId.Sink) return AssemblyFailure.UnknownInstance;
            return port.Port == 0 ? AssemblyFailure.None : AssemblyFailure.UnknownPort;
        }
        var index = IndexOf(port.Instance);
        if (index < 0) return AssemblyFailure.UnknownInstance;
        return port.Port >= 0 && port.Port < _parts[index].Part.Connectors.Count ? AssemblyFailure.None : AssemblyFailure.UnknownPort;
    }

    private int IndexOf(PartInstanceId instance)
    {
        for (var i = 0; i < _parts.Count; i++)
        {
            if (_parts[i].Instance == instance) return i;
        }
        return -1;
    }

    private static int Compare(PortRef a, PortRef b)
    {
        var byInstance = a.Instance.Value.CompareTo(b.Instance.Value);
        return byInstance != 0 ? byInstance : a.Port.CompareTo(b.Port);
    }
}
