using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.CrewFeed;

/// <summary>A feed reaction for one crew member, or for everyone when <see cref="Crew"/> is null.</summary>
public readonly record struct CrewFeedNotice(CrewId? Crew, CrewFeedEvent Event)
{
    public bool AppliesTo(CrewId crew) => Crew is null || Crew.Value == crew;
}
