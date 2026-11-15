using Usa.Game.Presentation.Diagram;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using SimComponentState = Usa.Simulation.Knowledge.ComponentKnowledgeState;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// The diagram's component states from the knowledge base only (GDD 4.4): the same four looks as the 3D view
/// (<see cref="AdapterMaps.Diagram"/>), the damage variant, an orthogonal stale flag and the confirming report
/// ("June Okafor · inspection"). <see cref="KnowledgeRevision"/> moves when any component the diagram has asked about
/// changes; it is re-checked when the journal grows or MET moves on (staleness changes with time alone).
/// </summary>
public sealed class DiagramKnowledgeLookup : IDiagramKnowledgeSource
{
    private readonly KnowledgeBase _knowledge;
    private readonly CrewNames? _crew;
    private readonly AdapterText _text;
    private readonly Dictionary<ComponentId, DiagramKnowledgeEntry> _seen = [];
    private readonly List<ComponentId> _seenIds = [];
    private int _revision;
    private int _checkedJournal = -1;
    private long _checkedSecond = long.MinValue;

    /// <param name="crew">Display names for the confirming crew member; without them the observer id is shown in words.</param>
    public DiagramKnowledgeLookup(KnowledgeBase knowledge, CrewNames? crew = null, AdapterText? text = null)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        _knowledge = knowledge;
        _crew = crew;
        _text = text ?? AdapterText.Default;
    }

    public int KnowledgeRevision
    {
        get
        {
            var second = _knowledge.Now.Milliseconds / 1000;
            if (_knowledge.Journal.Count == _checkedJournal && second == _checkedSecond) return _revision;
            _checkedJournal = _knowledge.Journal.Count;
            _checkedSecond = second;
            var changed = false;
            foreach (var id in _seenIds)
            {
                var current = Describe(id);
                if (current == _seen[id]) continue;
                _seen[id] = current;
                changed = true;
            }
            if (changed) _revision++;
            return _revision;
        }
    }

    public MissionTime Now => _knowledge.Now;

    public DiagramKnowledgeEntry Lookup(ComponentId component)
    {
        var entry = Describe(component);
        if (_seen.TryGetValue(component, out var previous))
        {
            // A lookup that sees a change first must still move the revision, or the next poll would miss it.
            if (previous != entry) _revision++;
        }
        else
        {
            _seenIds.Add(component);
        }
        _seen[component] = entry;
        return entry;
    }

    private DiagramKnowledgeEntry Describe(ComponentId component)
    {
        var known = _knowledge.ComponentOf(component);
        var confirmation = known.IsRevealed ? known.ConfirmedBy : null;
        return new DiagramKnowledgeEntry(
            AdapterMaps.Diagram(known.State),
            known.State == SimComponentState.ConfirmedDamaged ? known.Variant : null,
            known.IsStale && known.IsRevealed,
            confirmation?.Time,
            confirmation is null ? null : ConfirmedBy(confirmation));
    }

    private string ConfirmedBy(Observation confirmation)
    {
        var method = _text.Get(confirmation.Method switch
        {
            ObservationMethod.Glance => "console.methodGlance",
            ObservationMethod.Instrument => "console.methodInstrument",
            ObservationMethod.Inspection => "console.methodInspection",
            ObservationMethod.Test => "console.methodTest",
            ObservationMethod.Downlink => "console.methodDownlink",
            ObservationMethod.Model => "console.methodModel",
            ObservationMethod.Telemetry => "console.methodTelemetry",
            _ => throw new ArgumentOutOfRangeException(nameof(confirmation), confirmation.Method, null),
        });
        if (confirmation.Observer is not { } observer) return method;
        var name = _crew?.FullName(observer) ?? AdapterText.Words(observer.Value);
        return _text.Format("console.confirmedBy", ("observer", name), ("method", method));
    }
}
