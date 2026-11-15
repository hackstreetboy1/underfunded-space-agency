using Godot;
using Usa.Game.Presentation.Rendering;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// Feeds every <see cref="ComponentPresenter"/> from <see cref="ISpacecraftViewSource.GetComponentKnowledge"/>
/// (GDD 4.4). It reads only when <see cref="ISpacecraftViewSource.KnowledgeRevision"/> moves and touches only
/// presenters whose knowledge changed, so a reveal plays exactly once, on the change. The first pass after
/// <see cref="Reset"/> applies without animation (loading a save is not a discovery). Components the vehicle data
/// marks not revealable are drawn as built.
/// </summary>
public sealed partial class KnowledgeBinding : Node
{
    /// <summary>A component newly became revealed, or newly showed a confirmed damage variant.</summary>
    [Signal]
    public delegate void ComponentRevealedEventHandler(string componentId);

    private readonly Dictionary<string, ComponentPresenter> _presenters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentKnowledgeView> _applied = new(StringComparer.Ordinal);
    private int _revision;
    private bool _primed;

    public string? LastRevealedComponentId { get; private set; }

    public IReadOnlyDictionary<string, ComponentPresenter> Presenters => _presenters;

    public void Bind(IReadOnlyDictionary<string, ComponentPresenter> presenters)
    {
        ArgumentNullException.ThrowIfNull(presenters);
        foreach (var (id, presenter) in presenters)
        {
            if (!_presenters.TryAdd(id, presenter)) GD.PushError($"[KnowledgeBinding] component '{id}' is on two vehicles; second ignored.");
        }
    }

    /// <summary>Forgets what was applied; the next <see cref="Refresh"/> reapplies everything without animation.</summary>
    public void Reset()
    {
        _applied.Clear();
        _primed = false;
        LastRevealedComponentId = null;
    }

    /// <summary>Cheap when nothing changed (one integer compare); call once per frame.</summary>
    public void Refresh(ISpacecraftViewSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var revision = source.KnowledgeRevision;
        if (_primed && revision == _revision) return;

        var animate = _primed;
        _revision = revision;
        _primed = true;

        foreach (var (id, presenter) in _presenters)
        {
            var knowledge = source.IsRevealable(id)
                ? source.GetComponentKnowledge(id)
                : new ComponentKnowledgeView(ComponentLookState.ConfirmedNominal, null, false);
            if (_applied.TryGetValue(id, out var applied) && applied == knowledge) continue;
            _applied[id] = knowledge;

            var before = (presenter.IsRevealed, presenter.Variant);
            presenter.SetKnowledge(knowledge.State, knowledge.Variant, knowledge.Stale, animate: animate);
            var newlyShown = presenter.IsRevealed &&
                (!before.IsRevealed || (presenter.Variant is not null && presenter.Variant != before.Variant));
            if (animate && newlyShown && source.IsRevealable(id))
            {
                LastRevealedComponentId = id;
                EmitSignal(SignalName.ComponentRevealed, id);
            }
        }
    }
}
