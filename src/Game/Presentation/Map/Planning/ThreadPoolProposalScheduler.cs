using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>Runs proposals on the .NET thread pool, off the frame; the game host passes this to <see cref="BurnPlannerModel"/>.</summary>
public sealed class ThreadPoolProposalScheduler : IProposalScheduler
{
    public Task<BurnProposal> Run(Func<BurnProposal> work, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Run(work, cancellation);
    }
}
