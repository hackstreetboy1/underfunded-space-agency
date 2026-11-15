namespace Usa.Simulation.Engineering;

/// <summary>The digital-test checks (GDD 8), in the order they are reported.</summary>
public enum CheckKind
{
    Continuity,
    ConnectorCompatibility,
    Protection,
    Length,
    ShortToGround,
    Inventory,
}
