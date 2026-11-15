namespace Usa.Simulation.Engineering;

public enum CheckStatus
{
    Passed,
    Failed,

    /// <summary>Not evaluated because it needs a complete source-to-sink circuit.</summary>
    Skipped,
}
