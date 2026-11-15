using Usa.Simulation.Kernel;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Presentation.Diagram;

/// <summary>
/// Interactive state of the internal diagram over a <see cref="DiagramLayout"/>: layer toggles, jettisoned sections
/// and absent vehicles (hidden), selection with supply-path highlighting, hover and hit testing. Node display state
/// comes only from the injected <see cref="IDiagramKnowledgeSource"/> (the ground's knowledge), never from the
/// vehicle simulation.
/// </summary>
public sealed class DiagramScene
{
    private const int MaxTooltipNames = 4;

    private readonly IDiagramKnowledgeSource _knowledge;
    private readonly HashSet<SectionKey> _hiddenSections = [];
    private readonly HashSet<string> _hiddenVehicles = new(StringComparer.Ordinal);
    private SupplyPath? _supplyPath;
    private int? _builtRevision;
    private long _builtMinute;
    private bool _builtWithStale;

    public DiagramScene(DiagramLayout layout, IDiagramKnowledgeSource knowledge)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(knowledge);
        Layout = layout;
        _knowledge = knowledge;
    }

    public DiagramLayout Layout { get; }

    public DiagramLayers Layers { get; private set; } = DiagramLayers.All;

    public ComponentId? Selected { get; private set; }

    public ComponentId? Hovered { get; private set; }

    /// <summary>Follow bus ties when lighting a supply path.</summary>
    public bool FollowBusTies
    {
        get;
        set
        {
            field = value;
            RefreshSupplyPath();
        }
    } = true;

    public IReadOnlyCollection<SectionKey> HiddenSections => _hiddenSections;

    public void SetLayers(DiagramLayers layers) => Layers = layers & DiagramLayers.All;

    public void ToggleLayer(DiagramLayers layer) => Layers = (Layers ^ layer) & DiagramLayers.All;

    /// <summary>Hides a separated section (from a <c>SectionJettisoned</c> event).</summary>
    public void HideSection(string vehicleId, string sectionId)
    {
        _hiddenSections.Add(new SectionKey(vehicleId, sectionId));
        RefreshSelection();
    }

    /// <summary>Hides the section whose staging id is <paramref name="jettisonEventId"/> (e.g. <c>equipment_sep</c>).</summary>
    /// <returns>False when <paramref name="vehicle"/> has no such section.</returns>
    public bool Jettison(VehicleDefinition vehicle, string jettisonEventId)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        foreach (var section in vehicle.Sections)
        {
            if (section.Jettison == jettisonEventId)
            {
                HideSection(vehicle.Id, section.Id);
                return true;
            }
        }
        return false;
    }

    /// <summary>Shows or hides a whole vehicle (e.g. the Lodestar after undocking).</summary>
    public void SetVehicleVisible(string vehicleId, bool visible)
    {
        if (visible) _hiddenVehicles.Remove(vehicleId);
        else _hiddenVehicles.Add(vehicleId);
        RefreshSelection();
    }

    public bool IsHidden(ComponentId id)
    {
        var section = Layout.SectionOf(id);
        return _hiddenVehicles.Contains(section.VehicleId) || _hiddenSections.Contains(section);
    }

    /// <summary>Selects a component (null clears) and lights its supply path.</summary>
    public void Select(ComponentId? component)
    {
        Selected = component is { } id && Layout.TryGetNode(id, out _) && !IsHidden(id) ? id : null;
        RefreshSupplyPath();
    }

    public void Hover(ComponentId? component) =>
        Hovered = component is { } id && Layout.TryGetNode(id, out _) && !IsHidden(id) ? id : null;

    /// <summary>The visible node nearest to <paramref name="point"/> within the hit radius, else the compartment under it.</summary>
    public DiagramHit HitTest(DiagramPoint point)
    {
        var radius = Layout.Options.NodeRadiusPx;
        DiagramNode? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var node in Layout.Nodes)
        {
            var distance = node.Position.DistanceTo(point);
            if (distance > radius || distance >= bestDistance || IsHidden(node.Id)) continue;
            best = node;
            bestDistance = distance;
        }
        if (best is not null) return new DiagramHit(best.Id, best.CompartmentId);

        foreach (var compartment in VisibleCompartments())
        {
            if (compartment.Bounds.Contains(point)) return new DiagramHit(null, compartment.Id);
        }
        return default;
    }

    public NodeDisplay DisplayOf(ComponentId id) => NodeDisplay.For(_knowledge.Lookup(id), _knowledge.Now);

    /// <summary>
    /// The last <see cref="BuildFrame"/> no longer matches the knowledge: its revision changed, or a stale badge's
    /// minute moved on. Poll it at the UI cadence (10 Hz) and rebuild only when true; selection, hover, layer and
    /// visibility changes are the caller's own and need a rebuild anyway.
    /// </summary>
    public bool KnowledgeOutdated =>
        _builtRevision != _knowledge.KnowledgeRevision || (_builtWithStale && MinuteOf(_knowledge.Now) != _builtMinute);

    public DiagramTooltip Tooltip(ComponentId id)
    {
        if (!Layout.TryGetNode(id, out var node)) throw new KeyNotFoundException($"Component '{id}' is not on the diagram.");
        var display = DisplayOf(id);
        var feeds = Layout.FeedsOf(id, IsHidden);
        var buses = Layout.BusesFeeding(id, IsHidden);
        string? feedsText = null;
        if (feeds.Count > 0)
        {
            var downstreamBuses = node.Family is ComponentFamily.FuelCell or ComponentFamily.Battery
                ? DirectBuses(id)
                : [];
            feedsText = "Feeds: " + (downstreamBuses.Count > 0 ? Names(downstreamBuses) + " → " : "") + Names(feeds);
        }
        var fedByText = buses.Count > 0 ? "Fed by: " + Names(buses) : null;
        var knowledge = _knowledge.Lookup(id);
        return new DiagramTooltip(
            node.Name, DiagramStyles.FamilyLabel(node.Family), display.Word, display.Badge, feedsText, fedByText,
            knowledge.ConfirmedBy, knowledge.ConfirmedAt);
    }

    public DiagramFrame BuildFrame()
    {
        var path = _supplyPath;
        var highlightedNodes = new HashSet<ComponentId>(path?.Components ?? []);
        var highlightedConnections = new HashSet<string>(path?.ConnectionIds ?? [], StringComparer.Ordinal);

        _builtRevision = _knowledge.KnowledgeRevision;
        _builtMinute = MinuteOf(_knowledge.Now);
        _builtWithStale = false;
        var nodes = new List<DiagramNodeView>(Layout.Nodes.Count);
        foreach (var node in Layout.Nodes)
        {
            if (IsHidden(node.Id)) continue;
            var display = DisplayOf(node.Id);
            _builtWithStale |= display.Stale;
            nodes.Add(new DiagramNodeView(node, display, node.Id == Selected, highlightedNodes.Contains(node.Id), node.Id == Hovered));
        }

        var connections = new List<DiagramConnectionView>();
        foreach (var connection in Layout.Connections)
        {
            if ((Layers & DiagramStyles.LayerOf(connection.Kind)) == 0) continue;
            if (IsHidden(connection.From) || IsHidden(connection.To)) continue;
            connections.Add(new DiagramConnectionView(connection, highlightedConnections.Contains(connection.Id)));
        }

        return new DiagramFrame(VisibleCompartments(), nodes, connections, Layers, Selected, path);
    }

    /// <summary>Compartments with at least one visible component.</summary>
    public IReadOnlyList<DiagramCompartment> VisibleCompartments()
    {
        var visible = new List<DiagramCompartment>(Layout.Compartments.Count);
        foreach (var compartment in Layout.Compartments)
        {
            foreach (var section in compartment.Sections)
            {
                if (!_hiddenVehicles.Contains(section.VehicleId) && !_hiddenSections.Contains(section))
                {
                    visible.Add(compartment);
                    break;
                }
            }
        }
        return visible;
    }

    private List<ComponentId> DirectBuses(ComponentId source)
    {
        var buses = new List<ComponentId>();
        foreach (var connection in Layout.Connections)
        {
            if (connection.From == source && connection.Kind == ConnectionKind.Power
                && Layout.TryGetNode(connection.To, out var target) && target.Family == ComponentFamily.Bus)
                buses.Add(connection.To);
        }
        return buses;
    }

    private string Names(IReadOnlyList<ComponentId> ids)
    {
        var shown = Math.Min(ids.Count, MaxTooltipNames);
        var names = new string[shown];
        for (var i = 0; i < shown; i++) names[i] = Layout.NameOf(ids[i]);
        var text = string.Join(", ", names);
        return ids.Count > shown ? $"{text} +{ids.Count - shown} more" : text;
    }

    private static long MinuteOf(MissionTime time) => time.Milliseconds / 60_000;

    private void RefreshSelection()
    {
        if (Selected is { } selected && IsHidden(selected)) Selected = null;
        if (Hovered is { } hovered && IsHidden(hovered)) Hovered = null;
        RefreshSupplyPath();
    }

    private void RefreshSupplyPath()
    {
        if (Selected is not { } selected)
        {
            _supplyPath = null;
            return;
        }
        var path = Layout.SupplyPathOf(selected, IsHidden, FollowBusTies);
        _supplyPath = path.IsEmpty ? null : path;
    }
}
