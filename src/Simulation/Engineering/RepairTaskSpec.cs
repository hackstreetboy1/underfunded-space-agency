using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <summary>
/// What a passing design asks the crew to do: submitted as the <c>install_repair</c> crew task. Reserve
/// <see cref="PartsUsed"/> when the task starts and settle with <see cref="Inventory.SettleRepair"/> when it ends.
/// </summary>
/// <param name="Repairs">The damaged component the bypass replaces.</param>
/// <param name="ConsumedOnFailure">Parts lost if the installation fails (subset of <see cref="PartsUsed"/>).</param>
public sealed record RepairTaskSpec(
    string GoalId,
    TaskId TaskId,
    string Skill,
    ComponentId Source,
    ComponentId Sink,
    ComponentId Repairs,
    double DurationS,
    int Complexity,
    int PartCount,
    IReadOnlyList<PartQuantity> PartsUsed,
    IReadOnlyList<PartQuantity> ConsumedOnFailure,
    AssemblyDesign Design);
