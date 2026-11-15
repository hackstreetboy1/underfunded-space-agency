using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>What the command pipeline needs to know about the crew. Implemented over the Crew area.</summary>
public interface ICrewDirectory
{
    /// <summary>Every crew member, in id order.</summary>
    IReadOnlyList<CrewId> Members { get; }

    bool IsAlive(CrewId crew);

    /// <summary>Alive and able to carry out work (not incapacitated).</summary>
    bool CanWork(CrewId crew);

    /// <summary>The task occupying the single crew task slot, or null when it is free.</summary>
    TaskId? ActiveTask { get; }

    /// <summary>The best member able to work, by skill for <paramref name="task"/> (null for a glance or report) and condition; null when nobody can.</summary>
    CrewId? BestAssignee(TaskId? task);
}

internal static class CrewDirectoryExtensions
{
    public static bool IsMember(this ICrewDirectory crew, CrewId id)
    {
        foreach (var member in crew.Members)
        {
            if (member == id) return true;
        }
        return false;
    }
}
