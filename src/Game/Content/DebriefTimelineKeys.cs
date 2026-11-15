using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Game.Content;

/// <summary>
/// The <see cref="DebriefDefinition.Explanations"/> keys a mission's debrief timeline can carry (GDD 10.4): one incident and
/// one discovery mark per incident of the mission, and one decision mark per crew-task or remote action of the catalog.
/// The recorder emits the incident and decision marks; <see cref="DebriefComposer"/> adds the discoveries.
/// </summary>
public static class DebriefTimelineKeys
{
    public const string Prefix = "timeline.";
    public const string IncidentPrefix = Prefix + "incident.";
    public const string DiscoveryPrefix = Prefix + "discovery.";
    public const string DecisionPrefix = Prefix + "decision.";

    /// <summary>
    /// Action kinds whose first execution with given arguments is a timeline decision; glance reads and session commands are not.
    /// Every decision mark carries its arguments, so any decision text may name one through a placeholder.
    /// </summary>
    public static IReadOnlyList<ActionKind> DecisionKinds { get; } = [ActionKind.CrewTask, ActionKind.Remote];

    public static string Incident(IncidentId incident) => IncidentPrefix + incident.Value;

    public static string Discovery(IncidentId incident) => DiscoveryPrefix + incident.Value;

    public static string Decision(ActionId action) => DecisionPrefix + action.Value;

    /// <summary>Every timeline key, sorted ordinally.</summary>
    public static IReadOnlyList<string> For(IEnumerable<IncidentId> incidents, IEnumerable<ActionDefinition> actions)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        ArgumentNullException.ThrowIfNull(actions);
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var incident in incidents)
        {
            keys.Add(Incident(incident));
            keys.Add(Discovery(incident));
        }
        foreach (var action in actions)
        {
            if (DecisionKinds.Contains(action.Kind)) keys.Add(Decision(action.Id));
        }
        return [.. keys];
    }

    /// <summary>
    /// Every timeline key of the mission, from its incidents and the action catalog (<see cref="DebriefMissionReferences.Load"/>).
    /// Null, with the problems added to <paramref name="errors"/>, when a file cannot be read.
    /// </summary>
    public static IReadOnlyList<string>? Load(IDataSource source, string missionId, DefinitionErrors errors) =>
        DebriefMissionReferences.Load(source, missionId, errors)?.TimelineKeys;
}
