using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>Runs the proposal on the calling thread before returning (headless runs, tests); the planner applies it at once.</summary>
public sealed class InlineProposalScheduler : IProposalScheduler
{
    public static InlineProposalScheduler Instance { get; } = new();

    public Task<BurnProposal> Run(Func<BurnProposal> work, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (cancellation.IsCancellationRequested) return Task.FromCanceled<BurnProposal>(cancellation);
        try
        {
            return Task.FromResult(work());
        }
        catch (Exception failure)
        {
            return Task.FromException<BurnProposal>(failure);
        }
    }
}
