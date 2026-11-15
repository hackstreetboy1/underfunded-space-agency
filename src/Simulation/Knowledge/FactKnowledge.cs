using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>Snapshot of what is known about one fact (the player's view: every report reaches the ground).</summary>
/// <param name="Latest">Latest crew measurement, fresh or stale.</param>
/// <param name="Estimate">Latest model value.</param>
/// <param name="ConflictsWith">Fresh observations that disagree with <paramref name="Latest"/> beyond tolerance.</param>
/// <param name="GroundBand">Latest downlinked band (coarse word only).</param>
public sealed record FactKnowledge(
    FactId Fact,
    KnowledgeState State,
    Observation? Latest,
    Observation? Estimate,
    IReadOnlyList<Observation> ConflictsWith,
    Observation? GroundBand)
{
    public string? GroundBandWord => GroundBand?.Qualitative;
}
