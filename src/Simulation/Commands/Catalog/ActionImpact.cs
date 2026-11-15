namespace Usa.Simulation.Commands.Catalog;

/// <summary>Ordered by severity: high and irreversible actions need player confirmation.</summary>
public enum ActionImpact
{
    Low,
    High,
    Irreversible,
}
