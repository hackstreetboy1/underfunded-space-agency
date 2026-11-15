namespace Usa.Simulation.Guidance;

/// <summary>Tip strength, weakest first (GDD 9.1).</summary>
public enum HintStage
{
    None = 0,
    Nudge = 1,
    Investigate = 2,
    Connect = 3,
    Recommend = 4,
    Direct = 5,
}
