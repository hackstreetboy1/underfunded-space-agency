using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>
/// Where the burn planner runs proposal work (<see cref="IBurnProposalPort.PrepareProposal"/>). The planner never waits
/// on the task: it polls it from <see cref="BurnPlannerModel.Update"/>, so the result is applied on the thread that
/// drives the planner, whichever thread computed it.
/// </summary>
public interface IProposalScheduler
{
    /// <param name="cancellation">Set when the request is replaced or abandoned; work not yet started should not start.</param>
    Task<BurnProposal> Run(Func<BurnProposal> work, CancellationToken cancellation);
}
