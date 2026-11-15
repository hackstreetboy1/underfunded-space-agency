using Usa.Simulation.Data;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Definitions;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Scenario;

public sealed record LoadedScenario(Mission Mission, IncidentSet Incidents);

/// <summary>Loads <c>mission.json</c> and its incidents file, compiles every expression and validates all cross-references.</summary>
public sealed class ScenarioLoader(IDataSource source, IExpressionCompiler compiler)
{
    public const string MissionSchema = "mission/1";
    public const string IncidentsSchema = "incidents/1";
    public const string IncidentsFileKey = "incidents";

    /// <param name="missionDirectory">Relative to the data root, e.g. <c>missions/out_of_control</c>.</param>
    /// <param name="knownState">
    /// When given, every expression path outside <c>mission.</c> and <c>variant.</c> must be known to it
    /// (pass the fully registered composite at boot). Those two prefixes are always checked against the mission itself.
    /// </param>
    /// <param name="checkConditionKinds">
    /// Also evaluate every condition (<c>when</c>, <c>complete</c>, <c>runWhile</c>, <c>resetWhen</c>, …) once against
    /// <paramref name="knownState"/> and reject one that gives a number or text (review SIM-2 S14). Only meaningful when
    /// <paramref name="knownState"/> holds real values (the composed session), not a path list.
    /// </param>
    /// <exception cref="DefinitionException">Lists every problem found.</exception>
    public LoadedScenario Load(string missionDirectory, IStateQuery? knownState = null, bool checkConditionKinds = false)
    {
        var loader = new DefinitionLoader(source);
        var missionFile = $"{missionDirectory}/mission.json";
        var missionDefinition = loader.Load<MissionDefinition>(missionFile, MissionSchema);

        var errors = new DefinitionErrors();
        if (!missionDefinition.Files.TryGetValue(IncidentsFileKey, out var incidentsName))
        {
            errors.Add(missionFile, "$.files", $"Missing '{IncidentsFileKey}' file.");
            errors.ThrowIfAny();
        }
        var incidentsFile = $"{missionDirectory}/{incidentsName}";
        var incidentsDefinition = loader.Load<IncidentsDefinition>(incidentsFile, IncidentsSchema);

        var expressions = new ExpressionCollector(compiler, errors);
        var mission = new MissionCompiler(missionFile, expressions, errors).Compile(missionDefinition);
        var incidents = new IncidentsCompiler(incidentsFile, expressions, errors, mission).Compile(incidentsDefinition);
        CheckPaths(expressions.Collected, mission, incidents, knownState, checkConditionKinds, errors);

        errors.ThrowIfAny();
        return new LoadedScenario(mission, incidents);
    }

    private static void CheckPaths(
        IReadOnlyList<CollectedExpression> collected,
        Mission mission,
        IncidentSet incidents,
        IStateQuery? knownState,
        bool checkConditionKinds,
        DefinitionErrors errors)
    {
        var missionPaths = new MissionPathTable(mission);
        var variantIds = incidents.Variants.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in collected)
        {
            var before = errors.All.Count;
            foreach (var path in item.Expression.Paths)
            {
                if (path.StartsWith(StatePaths.Mission, StringComparison.Ordinal))
                {
                    if (!missionPaths.Contains(path))
                        errors.Add(item.File, item.JsonPath, $"Unknown mission path '{path}'.");
                    else if (item.IsNamedCondition && path.StartsWith(MissionPathTable.ConditionPrefix, StringComparison.Ordinal))
                        errors.Add(item.File, item.JsonPath, $"A named condition cannot reference another condition ('{path}').");
                }
                else if (path.StartsWith(StatePaths.Variant, StringComparison.Ordinal))
                {
                    if (!variantIds.Contains(path[StatePaths.Variant.Length..]))
                        errors.Add(item.File, item.JsonPath, $"Unknown variant path '{path}'.");
                }
                else if (knownState is not null && !knownState.IsKnownPath(path))
                {
                    errors.Add(item.File, item.JsonPath, $"Unknown state path '{path}'.");
                }
            }
            if (checkConditionKinds && item.IsCondition && knownState is not null && errors.All.Count == before) CheckBoolean(item, knownState, errors);
        }
    }

    /// <summary>
    /// A condition must evaluate to a boolean: a number or text would throw mid-tick when the runtime reads it as true/false.
    /// Paths have no static kind, so it is evaluated once against the composed state; a value not yet available (none)
    /// passes, and evaluation failures are left to the whole-mission scan.
    /// </summary>
    private static void CheckBoolean(CollectedExpression item, IStateQuery state, DefinitionErrors errors)
    {
        StateValue value;
        try
        {
            value = item.Expression.Evaluate(state);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        if (value.Kind is StateValueKind.Number or StateValueKind.Text)
            errors.Add(item.File, item.JsonPath, $"\"{item.Expression.Source}\" must be a condition (true/false), but it gives the {value.Kind.ToString().ToLowerInvariant()} {value}.");
    }
}
