using Usa.Simulation.Kernel;

namespace Usa.Game.Content;

/// <summary>
/// The per-objective decision keys the session's mission record puts on best decisions and avoidable losses
/// (<c>objective_completed.&lt;objective&gt;</c>, <c>objective_lost.&lt;objective&gt;</c>, one pair per primary objective).
/// They are explanation keys of <see cref="DebriefDefinition.Explanations"/>; an avoidable loss's text is also the subject of
/// the <c>decisions.avoidable_loss</c> note.
/// </summary>
public static class ScenarioDecisionKeys
{
    public const string ObjectiveCompletedPrefix = "objective_completed.";
    public const string ObjectiveLostPrefix = "objective_lost.";

    public static string ObjectiveCompleted(ObjectiveId objective) => ObjectiveCompletedPrefix + objective.Value;

    public static string ObjectiveLost(ObjectiveId objective) => ObjectiveLostPrefix + objective.Value;

    /// <summary>Both keys of every primary objective, sorted ordinally.</summary>
    public static IReadOnlyList<string> For(IEnumerable<ObjectiveId> primaryObjectives)
    {
        ArgumentNullException.ThrowIfNull(primaryObjectives);
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var objective in primaryObjectives)
        {
            keys.Add(ObjectiveCompleted(objective));
            keys.Add(ObjectiveLost(objective));
        }
        return [.. keys];
    }
}
