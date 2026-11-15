using Usa.Simulation.Kernel;

namespace Usa.Game.Hosting;

/// <summary>What one <see cref="SimulationPacer.Step"/> did, for diagnostics and tests.</summary>
/// <param name="TicksRun">Simulation ticks run this frame.</param>
/// <param name="MetBefore">Session MET when the frame started.</param>
/// <param name="MetAfter">Session MET when the frame ended.</param>
/// <param name="BudgetExhausted">The frame budget stopped the loop before the target was reached.</param>
/// <param name="DroppedMs">MET backlog discarded by the clamp this frame.</param>
/// <param name="Frozen">The session was frozen; nothing ran.</param>
public readonly record struct PacerFrame(
    int TicksRun,
    MissionTime MetBefore,
    MissionTime MetAfter,
    bool BudgetExhausted,
    double DroppedMs,
    bool Frozen);
