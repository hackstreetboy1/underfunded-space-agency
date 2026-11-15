namespace Usa.Simulation.Session.Scripts;

/// <summary>
/// Event keys a scenario can wait for or expect, as <c>kind:subject</c>:
/// <list type="table">
/// <item><term>incident:&lt;incidentId&gt;</term><description>the incident triggered</description></item>
/// <item><term>event:&lt;missionEventId&gt;</term><description>a mission event was raised (<c>docked</c>, <c>retrofire</c>, ...)</description></item>
/// <item><term>phase:&lt;phase&gt;</term><description>the mission entered the phase</description></item>
/// <item><term>objective:&lt;id&gt;</term><description>the objective was completed</description></item>
/// <item><term>done: / failed: / stopped: / refused:&lt;label&gt;</term><description>the labelled step's crew task completed, failed its error
/// draw, was stopped, or the step was refused; non-task commands are <c>done</c> when accepted</description></item>
/// <item><term>task_done: / task_failed:&lt;taskId&gt;</term><description>any crew task of that id completed or failed</description></item>
/// <item><term>observation:&lt;factId&gt;</term><description>an observation of the fact was recorded</description></item>
/// <item><term>alarm:&lt;alarmId&gt;</term><description>the alarm was raised</description></item>
/// <item><term>crew_died:&lt;crewId&gt;</term><description>the astronaut died</description></item>
/// <item><term>ended:&lt;outcome&gt;</term><description>the mission ended (<c>recovered</c>, <c>all_crew_lost</c>)</description></item>
/// </list>
/// </summary>
public static class ScenarioEventKeys
{
    private static readonly string[] Kinds =
    [
        "incident", "event", "phase", "objective", "done", "failed", "stopped", "refused", "task_done", "task_failed", "observation",
        "alarm", "crew_died", "ended",
    ];

    public static bool IsWellFormed(string key)
    {
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && colon < key.Length - 1 && Array.IndexOf(Kinds, key[..colon]) >= 0;
    }

    public static string Of(string kind, string subject) => $"{kind}:{subject}";
}
