using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>A short instrument read: <paramref name="Reader"/> reports <paramref name="Facts"/> at <paramref name="ReadyAt"/>.</summary>
public sealed record GlanceRead(CommandId Origin, CrewId Reader, IReadOnlyList<FactId> Facts, MissionTime RequestedAt, MissionTime ReadyAt);

/// <summary>Schedules delayed glance observations. Implemented over the Knowledge area.</summary>
public interface IGlanceReadExecutor
{
    void Schedule(GlanceRead read);
}
