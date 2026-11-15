using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>The Easy-mode "Propose burn" assist.</summary>
public interface IBurnProposalPort
{
    /// <summary>
    /// Captures the flight picture now, on the caller's thread, and returns the proposal work over that copy alone: it
    /// shares nothing mutable with the live session, so an <see cref="IProposalScheduler"/> may run it on a worker.
    /// </summary>
    /// <exception cref="PreviewUnavailableException">There is no vehicle to plan for.</exception>
    Func<BurnProposal> PrepareProposal(BurnGoal goal, BurnEngine engine, MissionTime earliestStart);
}
