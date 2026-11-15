using Usa.Simulation.Crew.Tasks;

namespace Usa.Game.Presentation.CrewFeed;

public static class CrewActivities
{
    public static CrewActivity From(TaskKind kind) => kind switch
    {
        TaskKind.Operate => CrewActivity.Operate,
        TaskKind.Inspection => CrewActivity.Inspection,
        TaskKind.Test => CrewActivity.Test,
        TaskKind.Repair => CrewActivity.Repair,
        TaskKind.Eva => CrewActivity.Eva,
        TaskKind.Procedure => CrewActivity.Procedure,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Hands-on work shown with <c>feed_work</c>.</summary>
    public static bool IsHandsOn(this CrewActivity activity) => activity is not (CrewActivity.None or CrewActivity.GlanceRead);
}
