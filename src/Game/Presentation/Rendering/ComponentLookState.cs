namespace Usa.Game.Presentation.Rendering;

/// <summary>
/// What the player knows about a component, as far as the 3D view cares. Mirrors
/// <c>Usa.Simulation.Knowledge.ComponentKnowledgeState</c> member for member, so the view adapter maps
/// it with a plain cast while this folder builds without the Knowledge area.
/// </summary>
public enum ComponentLookState
{
    Unknown,
    Suspected,
    ConfirmedNominal,
    ConfirmedDamaged,
}
