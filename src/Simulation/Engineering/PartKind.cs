namespace Usa.Simulation.Engineering;

public enum PartKind
{
    Harness,
    Adapter,
    Fuse,
    Breaker,
    Tape,
    Canister,
    Consumable,

    /// <summary>Required by tasks and repairs but never used up (cutter, pry bar, hand thermometer).</summary>
    Tool,
}
