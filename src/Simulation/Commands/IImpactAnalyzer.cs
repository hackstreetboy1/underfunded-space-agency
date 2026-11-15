namespace Usa.Simulation.Commands;

/// <summary>Predicts which objectives a high-impact action would change. Implemented over the Scenario area's feasibility rules.</summary>
public interface IImpactAnalyzer
{
    /// <summary>Changes in objective id order; empty when none.</summary>
    IReadOnlyList<ObjectiveFeasibilityChange> PredictObjectiveChanges(ResolvedCommand command);
}
