using Usa.Simulation.Crew;
using Usa.Simulation.Guidance;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;

namespace Usa.Simulation.Session;

/// <summary>
/// Everything <see cref="SnapshotBuilder"/> would otherwise rebuild on every host read: the fact list it reports (crew
/// condition facts resolved once), objective state paths, and the mission's pacing data resolved against its declared phases
/// and events. Built once at composition; holds no simulation state.
/// </summary>
internal sealed class SessionReadCache
{
    public SessionReadCache(MissionData data, CrewRoster roster, GuidanceSettings guidance)
    {
        var catalog = data.Facts;
        var facts = new List<FactDefinition>(catalog.Facts.Count + roster.Members.Count);
        facts.AddRange(catalog.Facts);
        foreach (var member in roster.Members)
        {
            if (catalog.TryResolve(catalog.CrewConditionFact(member.Id), out var fact)) facts.Add(fact);
        }
        Facts = [.. facts];

        var objectives = data.Scenario.Mission.Objectives;
        ObjectiveStatePaths = new string[objectives.Count];
        ObjectiveCausePaths = new string[objectives.Count];
        ObjectiveKinds = new string[objectives.Count];
        for (var i = 0; i < objectives.Count; i++)
        {
            ObjectiveStatePaths[i] = $"{StatePaths.Mission}objective.{objectives[i].Id}.state";
            ObjectiveCausePaths[i] = $"{StatePaths.Mission}objective.{objectives[i].Id}.cause";
            ObjectiveKinds[i] = objectives[i].Kind.ToString().ToLowerInvariant();
        }

        var mission = data.Scenario.Mission;
        var declared = mission.Events.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var pacing = data.SessionConfig.Pacing;
        DecisiveEvents = [.. (pacing?.DecisiveEvents ?? []).Where(declared.Contains)];
        if (pacing?.CalmOpening is { } opening)
        {
            CalmPhases = [.. opening.Phases];
            CalmUntilEvent = opening.UntilEvent is { } until && declared.Contains(until) ? until : null;
        }
        Guidance = guidance;
    }

    public const string OutcomePath = StatePaths.Mission + "outcome";

    /// <summary>Concrete facts in id order, then each crew member's condition fact.</summary>
    public FactDefinition[] Facts { get; }

    public string[] ObjectiveStatePaths { get; }

    public string[] ObjectiveCausePaths { get; }

    public string[] ObjectiveKinds { get; }

    /// <summary>Declared mission events that count as decisive moments.</summary>
    public string[] DecisiveEvents { get; }

    /// <summary>Phases of the calm opening; empty when the mission has none.</summary>
    public string[] CalmPhases { get; } = [];

    public string? CalmUntilEvent { get; }

    public GuidanceSettings Guidance { get; }

    public bool IsCalmOpening(MissionDirector director) =>
        Array.IndexOf(CalmPhases, director.Phase) >= 0 && (CalmUntilEvent is null || !director.HasFired(CalmUntilEvent));
}
