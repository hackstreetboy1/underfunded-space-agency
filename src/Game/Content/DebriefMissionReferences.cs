using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Data;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Scenario;
using Usa.Simulation.Scenario.Definitions;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Content;

/// <summary>
/// What a mission's debrief texts refer to, read from the mission and catalog files: incidents and the faults they set,
/// the crew-task and remote actions, primary objectives, recovery zones, fact labels and component names.
/// <see cref="DebriefLoader"/> validates the debrief against it and <see cref="DebriefComposer"/> resolves display names from it.
/// </summary>
public sealed class DebriefMissionReferences
{
    private readonly Dictionary<string, string> _componentNames = new(StringComparer.Ordinal);
    private readonly Dictionary<ActionId, ActionDefinition> _decisionActions = [];

    /// <param name="incidentFaults">Damage variant words the incidents set (<c>&lt;component&gt;.fault</c> effects).</param>
    /// <param name="componentNames">Display name per component id of the mission's vehicles.</param>
    public DebriefMissionReferences(
        IEnumerable<IncidentId> incidents,
        IEnumerable<ActionDefinition> actions,
        IEnumerable<ObjectiveId> primaryObjectives,
        IEnumerable<string> recoveryZones,
        IEnumerable<string> incidentFaults,
        FactCatalog facts,
        IEnumerable<KeyValuePair<string, string>> componentNames)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(componentNames);
        Incidents = [.. incidents];
        PrimaryObjectives = [.. primaryObjectives];
        RecoveryZones = [.. recoveryZones];
        IncidentFaults = [.. incidentFaults.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        foreach (var (id, name) in componentNames) _componentNames.TryAdd(id, name);

        var catalog = actions.ToArray();
        foreach (var action in catalog)
        {
            if (DebriefTimelineKeys.DecisionKinds.Contains(action.Kind)) _decisionActions.TryAdd(action.Id, action);
        }
        TimelineKeys = DebriefTimelineKeys.For(Incidents, catalog);
        DecisionKeys = ScenarioDecisionKeys.For(PrimaryObjectives);
    }

    public IReadOnlyList<IncidentId> Incidents { get; }

    public IReadOnlyList<ObjectiveId> PrimaryObjectives { get; }

    public IReadOnlyList<string> RecoveryZones { get; }

    public IReadOnlyList<string> IncidentFaults { get; }

    public FactCatalog Facts { get; }

    /// <summary>Every timeline key the mission can produce (<see cref="DebriefTimelineKeys.For"/>).</summary>
    public IReadOnlyList<string> TimelineKeys { get; }

    /// <summary>Every scenario decision key the mission record can carry (<see cref="ScenarioDecisionKeys.For"/>).</summary>
    public IReadOnlyList<string> DecisionKeys { get; }

    /// <summary>The crew-task or remote action whose first execution is a timeline decision.</summary>
    public bool TryGetDecisionAction(ActionId id, out ActionDefinition action) => _decisionActions.TryGetValue(id, out action!);

    public string? ComponentName(string componentId) => _componentNames.GetValueOrDefault(componentId);

    public bool IsComponent(string componentId) => _componentNames.ContainsKey(componentId);

    /// <summary>The catalog definition of a concrete fact id (patterns resolved); null for an unknown or malformed id.</summary>
    public FactDefinition? Fact(string factId)
    {
        if (string.IsNullOrEmpty(factId) || !Facts.IsKnownFact(factId)) return null;
        return Facts.TryResolve(new FactId(factId), out var definition) ? definition : null;
    }

    /// <summary>
    /// Reads the mission, its incidents and flight files, its vehicles, and the fact and action catalogs. Null, with every
    /// problem added to <paramref name="errors"/>, when any of them cannot be read (their own loaders validate the rest).
    /// </summary>
    public static DebriefMissionReferences? Load(IDataSource source, string missionId, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentNullException.ThrowIfNull(errors);
        // The mission's own actions and facts live in its catalog folder; the view merges them into the global catalogs.
        source = MissionDataSource.For(source, missionId);
        var loader = new DefinitionLoader(source);
        var directory = MissionData.MissionDirectory(missionId);

        var missionFile = $"{directory}/mission.json";
        var mission = ContentChecks.TryLoad(errors, missionFile,
            () => loader.Load<MissionDefinition>(missionFile, ScenarioLoader.MissionSchema));
        IncidentsDefinition? incidents = null;
        IReadOnlyList<VehicleDefinition>? vehicles = null;
        if (mission is not null)
        {
            if (mission.Files.TryGetValue(ScenarioLoader.IncidentsFileKey, out var name))
            {
                var incidentsFile = $"{directory}/{name}";
                incidents = ContentChecks.TryLoad(errors, incidentsFile,
                    () => loader.Load<IncidentsDefinition>(incidentsFile, ScenarioLoader.IncidentsSchema));
            }
            else
            {
                errors.Add(missionFile, "$.files", $"Missing '{ScenarioLoader.IncidentsFileKey}' file.");
            }

            vehicles = ContentChecks.TryLoad(errors, "vehicles",
                () => new VehicleDefinitionLoader(loader).LoadAll([.. mission.Vehicles.Select(v => $"vehicles/{v}.json")]));
        }

        var flightFile = $"{directory}/{MissionFlight.FileName}";
        var flight = ContentChecks.TryLoad(errors, flightFile,
            () => loader.Load<MissionFlightDefinition>(flightFile, MissionFlightDefinition.SchemaTag));
        var actions = ContentChecks.TryLoad(errors, ActionsFile.DefaultPath,
            () => loader.Load<ActionsFile>(ActionsFile.DefaultPath, ActionsFile.SchemaTag));
        var facts = ContentChecks.TryLoad(errors, FactCatalog.RelativePath, () => FactCatalog.Load(source));

        if (mission is null || incidents is null || vehicles is null || flight is null || actions is null || facts is null) return null;
        return new DebriefMissionReferences(
            incidents.Incidents.Select(i => i.Id),
            actions.Actions,
            mission.Objectives.Where(o => o.Kind == ObjectiveKind.Primary).Select(o => o.Id),
            flight.RecoveryZones.Select(z => z.Id),
            incidents.Incidents.SelectMany(i => i.Effects).Select(FaultSet).OfType<string>(),
            facts,
            vehicles.SelectMany(v => v.Components).Select(c => KeyValuePair.Create(c.Id.Value, c.Name)));
    }

    /// <summary>The variant word of a <c>&lt;component&gt;.fault = 'word'</c> effect; null for any other effect.</summary>
    private static string? FaultSet(IncidentEffectDefinition effect)
    {
        const string FaultField = ".fault";
        var value = effect.Value.Trim();
        return effect.Target.EndsWith(FaultField, StringComparison.Ordinal) && value.Length > 2 && value[0] == '\'' && value[^1] == '\''
            ? value[1..^1]
            : null;
    }
}
