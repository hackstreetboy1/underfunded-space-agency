using Godot;
using Usa.Game.Presentation.CrewFeed.Hosting;
using Usa.Simulation.Kernel;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary>Binds a comm-feed scene to one crew member's feed of a composed runtime.</summary>
public static class CrewFeedBinding
{
    /// <summary>
    /// <paramref name="animator"/> follows <paramref name="crew"/>'s feed, applied each frame after the runtime's tick, until the
    /// context is disposed; teardown then resets it (a lost crew member's feed comes back live for the next run).
    /// </summary>
    /// <returns>False when the mission has no such crew member (the animator is left reset).</returns>
    public static bool BindCrewFeed(this MissionRuntimeContext context, CrewId crew, CrewFeedAnimator animator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(animator);
        animator.Reset();
        if (!context.CrewFeeds.Feeds.TryGetValue(crew, out var feed)) return false;
        animator.ProcessPriority = MissionRuntimeTicker.AfterSimulationHost + 1;
        animator.Follow(feed);
        context.Own(new ResetFeed(animator));
        return true;
    }

    private sealed class ResetFeed(CrewFeedAnimator animator) : IDisposable
    {
        public void Dispose()
        {
            if (GodotObject.IsInstanceValid(animator)) animator.Reset();
        }
    }
}
