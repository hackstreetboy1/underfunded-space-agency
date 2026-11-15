using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>What the confirmation prompt shows before a high-impact or irreversible action.</summary>
/// <param name="EffectTextKeys">Localisation keys for the consequences, in data order.</param>
public sealed record ImpactSummary(
    ActionImpact Impact,
    IReadOnlyList<string> EffectTextKeys,
    IReadOnlyList<ComponentId> AffectedComponents,
    IReadOnlyList<ObjectiveFeasibilityChange> ObjectiveChanges);

/// <summary>An objective whose feasibility state the action would change, e.g. achievable → unacceptable_risk.</summary>
public sealed record ObjectiveFeasibilityChange(ObjectiveId Objective, string CurrentState, string PredictedState);
