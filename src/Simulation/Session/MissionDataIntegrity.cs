using System.Text.Json;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Data;
using Usa.Simulation.Expressions;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;

namespace Usa.Simulation.Session;

/// <summary>
/// Whole-mission data check against the composed session: every file loads; every expression in every data file compiles
/// with the real compiler, reads only known paths and evaluates once without a kind error (review X11); every effect target
/// is routable; action ↔ task ↔ fact ↔ component ↔ part cross-references resolve; every fact has a readable value; the
/// health exposure, sensor and telemetry paths exist; the mission's <c>session.json</c> names known tasks, events, rules and
/// paths. Only the shared files and this mission's own (its directory, its vehicles) are scanned, so another mission's data
/// never fails this one. Returns every problem found (empty when the data is consistent).
/// </summary>
public static class MissionDataIntegrity
{
    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly string[] Directories = ["catalog", "config", "crew", "difficulty"];

    public static IReadOnlyList<DefinitionError> Check(IDataSource source, string missionId, string difficultyId, ulong seed = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        var view = MissionDataSource.For(source, missionId);
        source = view;
        var errors = new DefinitionErrors();
        var compiler = new ExpressionCompiler();
        var data = MissionData.TryLoad(source, missionId, difficultyId, compiler, errors);
        if (data is null) return errors.All;

        SessionParts parts;
        try
        {
            parts = SessionComposer.Compose(data, seed, compiler, validate: false);
        }
        catch (Exception ex) when (ex is DefinitionException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            errors.Add("session", "$", $"Composition failed: {ex.GetType().Name}: {ex.Message}");
            return errors.All;
        }

        CheckAreaReferences(source, data, parts, compiler, errors);
        CheckCrossReferences(data, parts, errors);
        CheckFactValues(data, parts, errors);
        CheckIncidentTargets(data, parts, errors);
        CheckSessionConfig(data, parts, errors);
        foreach (var warning in data.Flight.Warnings) errors.Add(warning.File, warning.JsonPath, warning.Message);
        foreach (var file in DataFiles(view.Inner, data)) ScanFile(view.Inner, file, parts, compiler, errors);
        return errors.All;
    }

    private static void CheckAreaReferences(IDataSource source, MissionData data, SessionParts parts, IExpressionCompiler compiler, DefinitionErrors errors)
    {
        var loader = new DefinitionLoader(source);
        var state = parts.State;
        var components = parts.Vehicles.AllComponents().ToHashSet();
        var tasks = data.Tasks.Tasks.Select(t => t.Id).ToHashSet();
        var facts = data.Facts.Facts.Select(f => f.Id).Concat(parts.Roster.Members.Select(m => data.Facts.CrewConditionFact(m.Id))).ToHashSet();

        Collect(errors, () => new ScenarioLoader(source, compiler).Load(MissionData.MissionDirectory(data.MissionId), state, checkConditionKinds: true));
        Collect(errors, () => ActionCatalog.Load(loader, compiler,
            new ActionCatalogChecks(state, tasks, facts, data.Scenario.Mission.Phases.ToHashSet(StringComparer.Ordinal), components)));
        data.Tasks.ValidateBindings(parts.Binder, new TaskReferenceChecks
        {
            IsKnownPart = data.Parts.Contains,
            IsKnownFact = id => data.Facts.IsKnownFact(id.Value),
            IsKnownComponent = parts.Vehicles.IsKnownComponent,
            State = state,
            CanApplyEffect = parts.Router.CanApply,
            Locations = data.Locations,
        }, errors);
        data.Facts.ValidateReferences(new FactReferenceContext(components, parts.Roster.Members.Select(m => m.Id).ToHashSet(), tasks, state), errors);
        parts.ExposurePaths.Validate(errors);

        var references = new GuidanceReferences
        {
            IsKnownFact = id => data.Facts.IsKnownFact(id),
            IsKnownAction = id => data.Actions.Actions.Any(a => a.Id.Value == id),
            State = state,
        };
        Collect(errors, () => HintTreeLoader.Load(loader, data.MissionId, compiler, references));
        Collect(errors, () => TutorialLoader.Load(loader, data.MissionId, compiler, references));

        var repairsFile = Engineering.RepairCatalog.PathFor(data.MissionId);
        foreach (var path in data.Repairs.StatePathsUsed())
        {
            if (!state.IsKnownPath(path)) errors.Add(repairsFile, "$", $"Unknown state path '{path}'.");
        }
        foreach (var goal in data.Repairs.Goals)
        {
            if (!components.Contains(goal.Source.Component)) errors.Add(repairsFile, $"$.goals[{goal.Id}].source", $"Unknown component '{goal.Source.Component}'.");
            foreach (var sink in goal.SinkCandidates)
            {
                if (!components.Contains(sink.Definition.Component)) errors.Add(repairsFile, $"$.goals[{goal.Id}].sinkCandidates", $"Unknown component '{sink.Definition.Component}'.");
                if (!components.Contains(sink.Definition.Repairs)) errors.Add(repairsFile, $"$.goals[{goal.Id}].sinkCandidates", $"Unknown component '{sink.Definition.Repairs}'.");
            }
        }
    }

    private static void CheckCrossReferences(MissionData data, SessionParts parts, DefinitionErrors errors)
    {
        const string actionsFile = ActionsFile.DefaultPath;
        const string tasksFile = TaskCatalog.DefaultPath;
        var actionIds = data.Actions.Actions.ToDictionary(a => a.Id.Value, StringComparer.Ordinal);

        foreach (var action in data.Actions.Actions)
        {
            if (action.Kind != ActionKind.CrewTask) continue;
            if (action.Definition.TaskId is not { } taskId)
            {
                errors.Add(actionsFile, $"$.actions[{action.Id}]", "A crew_task action needs a taskId.");
                continue;
            }
            if (!data.Tasks.TryGet(taskId, out var task))
            {
                errors.Add(actionsFile, $"$.actions[{action.Id}].taskId", $"Unknown task '{taskId}'.");
                continue;
            }
            CheckParameters(action, task, errors);
        }

        foreach (var task in data.Tasks.Tasks)
        {
            if (!actionIds.TryGetValue(task.Id.Value, out var action) || action.Definition.TaskId != task.Id)
                errors.Add(tasksFile, $"$.tasks[{task.Id}]", $"No crew_task action '{task.Id}' starts this task (contract 5: every crew task has an action with the same id).");
        }

        foreach (var spec in data.Scenario.Mission.Events)
        {
            if (spec.Source is not { } sourceText || !sourceText.StartsWith(MissionEventIds.TaskSourcePrefix, StringComparison.Ordinal)) continue;
            var taskId = sourceText[MissionEventIds.TaskSourcePrefix.Length..].Trim();
            if (!data.Tasks.Tasks.Any(t => t.Id.Value == taskId))
                errors.Add($"{MissionData.MissionDirectory(data.MissionId)}/mission.json", $"$.events[{spec.Id}].source", $"Unknown task '{taskId}'.");
        }

        foreach (var member in data.Scenario.Mission.Crew)
        {
            if (!parts.Roster.TryFind(member, out _))
                errors.Add($"{MissionData.MissionDirectory(data.MissionId)}/mission.json", "$.crew", $"Unknown crew member '{member}'.");
        }
    }

    /// <summary>An action's parameters must reach its task under the same name with a value the task accepts.</summary>
    private static void CheckParameters(CatalogAction action, TaskDefinition task, DefinitionErrors errors)
    {
        const string file = ActionsFile.DefaultPath;
        foreach (var parameter in task.Parameters)
        {
            var actionParameter = action.FindParameter(parameter.Name);
            if (actionParameter is null)
            {
                errors.Add(file, $"$.actions[{action.Id}].parameters", $"Task '{task.Id}' needs parameter '{parameter.Name}', which the action does not have.");
                continue;
            }
            var rejected = parameter.Type == TaskParameterType.Choice
                ? (actionParameter.Values ?? []).Where(v => !parameter.Values.Contains(v, StringComparer.Ordinal)).ToArray()
                : [];
            if (rejected.Length > 0)
                errors.Add(file, $"$.actions[{action.Id}].parameters[{parameter.Name}]",
                    $"Task '{task.Id}' rejects {rejected.Length} of the action's values: {string.Join(", ", rejected)} " +
                    $"(task choices: {string.Join(", ", parameter.Values)}).");
            var compatible = (actionParameter.Type, parameter.Type) switch
            {
                (ParameterType.Enum, TaskParameterType.Choice) => true,
                (ParameterType.Boolean, TaskParameterType.Boolean) => true,
                (ParameterType.Number or ParameterType.Integer, TaskParameterType.Number) => true,
                (ParameterType.String or ParameterType.Enum, TaskParameterType.Identifier) => true,
                _ => false,
            };
            if (!compatible)
                errors.Add(file, $"$.actions[{action.Id}].parameters[{parameter.Name}]",
                    $"Action type '{actionParameter.Type}' does not match task '{task.Id}' parameter type '{parameter.Type}'.");
        }
        foreach (var parameter in action.Parameters)
        {
            if (!task.Parameters.Any(p => p.Name == parameter.Name))
                errors.Add(file, $"$.actions[{action.Id}].parameters[{parameter.Name}]", $"Task '{task.Id}' has no parameter '{parameter.Name}'.");
        }
    }

    private static void CheckFactValues(MissionData data, SessionParts parts, DefinitionErrors errors)
    {
        foreach (var fact in data.Facts.Facts)
        {
            if (parts.FactReader.Problem(fact) is { } problem) errors.Add(FactCatalog.RelativePath, $"$.facts[{fact.Id}]", problem);
        }
    }

    /// <summary>Every incident effect must be routable for every variant option, not only the seeded pick.</summary>
    private static void CheckIncidentTargets(MissionData data, SessionParts parts, DefinitionErrors errors)
    {
        var file = $"{MissionData.MissionDirectory(data.MissionId)}/incidents.json";
        foreach (var incident in data.Scenario.Incidents.Incidents)
        {
            foreach (var effect in incident.Effects)
            {
                foreach (var target in ExpandVariant(effect.Target, data))
                {
                    if (!parts.Router.CanApply(effect with { Target = target }))
                        errors.Add(file, $"$.incidents[{incident.Id}].effects", $"No handler accepts '{target}' ({effect.Operation}).");
                }
            }
        }
    }

    private static IEnumerable<string> ExpandVariant(string target, MissionData data)
    {
        const string open = "{variant.";
        if (!target.StartsWith(open, StringComparison.Ordinal)) return [target];
        var close = target.IndexOf('}', StringComparison.Ordinal);
        var id = target[open.Length..close];
        var variant = data.Scenario.Incidents.Variants.FirstOrDefault(v => v.Id == id);
        return variant is null ? [target] : variant.Options.Select(option => option + target[(close + 1)..]);
    }

    /// <summary>The shared files, the mission's own files (catalog included) and its vehicles, unmerged.</summary>
    private static IEnumerable<string> DataFiles(IDataSource source, MissionData data)
    {
        foreach (var directory in Directories)
        {
            foreach (var file in source.List(directory)) yield return file;
        }
        foreach (var file in Vehicle.Definitions.VehicleDefinitionLoader.FilesFor(data.Scenario.Mission.Vehicles)) yield return file;
        var missionDirectory = MissionData.MissionDirectory(data.MissionId);
        foreach (var file in source.List(missionDirectory)) yield return file;
        foreach (var file in source.List($"{missionDirectory}/catalog")) yield return file;
    }

    /// <summary>Everything <c>session.json</c> names must exist in the mission.</summary>
    private static void CheckSessionConfig(MissionData data, SessionParts parts, DefinitionErrors errors)
    {
        var config = data.SessionConfig;
        var file = MissionSessionConfig.PathFor(data.MissionId);
        var mission = data.Scenario.Mission;
        var events = mission.Events.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        void RequireTask(TaskId? task, string path)
        {
            if (task is { } id && !data.Tasks.TryGet(id, out _)) errors.Add(file, path, $"Unknown task '{id}'.");
        }
        RequireTask(config.EntryChecklist?.DrogueTask, "$.entryChecklist.drogueTask");
        RequireTask(config.EntryChecklist?.MainTask, "$.entryChecklist.mainTask");
        var dockingTasks = config.DockingTasks ?? [];
        for (var i = 0; i < dockingTasks.Count; i++) RequireTask(dockingTasks[i], $"$.dockingTasks[{i}]");
        if (config.Record?.StagingBeforeRetrofireEvent is { } staging && !events.Contains(staging))
            errors.Add(file, "$.record.stagingBeforeRetrofireEvent", $"mission.json declares no event '{staging}'.");
        if (config.Record?.DockingDamageRule is { } rule && !mission.Objectives.Any(o => o.Feasibility.Any(f => f.Id == rule)))
            errors.Add(file, "$.record.dockingDamageRule", $"No objective feasibility rule '{rule}'.");
        if (config.Report?.IncidentEvent is { } incident && !events.Contains(incident))
            errors.Add(file, "$.report.incidentEvent", $"mission.json declares no event '{incident}'.");
        var phases = mission.Phases.ToHashSet(StringComparer.Ordinal);
        if (config.Pacing?.CalmOpening is { } opening)
        {
            for (var i = 0; i < opening.Phases.Count; i++)
            {
                if (!phases.Contains(opening.Phases[i])) errors.Add(file, $"$.pacing.calmOpening.phases[{i}]", $"Unknown phase '{opening.Phases[i]}'.");
            }
            if (opening.UntilEvent is { } until && !events.Contains(until))
                errors.Add(file, "$.pacing.calmOpening.untilEvent", $"mission.json declares no event '{until}'.");
        }
        var decisive = config.Pacing?.DecisiveEvents ?? [];
        for (var i = 0; i < decisive.Count; i++)
        {
            if (!events.Contains(decisive[i])) errors.Add(file, $"$.pacing.decisiveEvents[{i}]", $"mission.json declares no event '{decisive[i]}'.");
        }
        var samples = config.Report?.Samples ?? [];
        for (var i = 0; i < samples.Count; i++)
        {
            if (!parts.State.IsKnownPath(samples[i].Path)) errors.Add(file, $"$.report.samples[{i}].path", $"Unknown state path '{samples[i].Path}'.");
        }
    }

    /// <summary>Generic scan of one file's JSON: expressions, effect targets and path-valued properties.</summary>
    private static void ScanFile(IDataSource source, string file, SessionParts parts, IExpressionCompiler compiler, DefinitionErrors errors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(source.ReadText(file), JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add(file, "$", ex.Message);
            return;
        }
        using (document) Walk(document.RootElement, "$", null, file, parts, compiler, errors);
    }

    private static void Walk(JsonElement element, string path, string? property, string file, SessionParts parts, IExpressionCompiler compiler, DefinitionErrors errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var child in element.EnumerateObject()) Walk(child.Value, $"{path}.{child.Name}", child.Name, file, parts, compiler, errors);
                CheckEffectObject(element, path, file, parts, errors);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var child in element.EnumerateArray()) Walk(child, $"{path}[{index++}]", property, file, parts, compiler, errors);
                break;
            case JsonValueKind.String:
                CheckString(element.GetString()!, path, property, file, parts, compiler, errors);
                break;
        }
    }

    private static void CheckString(string text, string path, string? property, string file, SessionParts parts, IExpressionCompiler compiler, DefinitionErrors errors)
    {
        if (text.Contains('{', StringComparison.Ordinal)) return;
        var state = parts.State;
        if (property is "measures" or "telemetryPath")
        {
            if (!state.IsKnownPath(text)) errors.Add(file, path, $"Unknown state path '{text}'.");
            return;
        }
        if (!text.Contains('@', StringComparison.Ordinal) || property is "description" or "text" or "label" or "name" or "title" or "source") return;

        IExpression expression;
        try
        {
            expression = compiler.Compile(text);
        }
        catch (ExpressionException ex)
        {
            errors.Add(file, path, ex.Message);
            return;
        }
        var unknown = expression.Paths.Where(p => !state.IsKnownPath(p)).ToArray();
        foreach (var p in unknown) errors.Add(file, path, $"Unknown state path '{p}' in \"{text}\".");
        if (unknown.Length > 0) return;
        try
        {
            expression.Evaluate(state);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(file, path, $"Evaluation fails against the composed state: {ex.Message}");
        }
    }

    /// <summary>Objects with a literal <c>target</c> and an operation are effects; their target must be routable.</summary>
    private static void CheckEffectObject(JsonElement element, string path, string file, SessionParts parts, DefinitionErrors errors)
    {
        if (!element.TryGetProperty("target", out var targetElement) || targetElement.ValueKind != JsonValueKind.String) return;
        var target = targetElement.GetString()!;
        if (target.Contains('{', StringComparison.Ordinal)) return;
        var opText = element.TryGetProperty("op", out var op) ? op.GetString()
            : element.TryGetProperty("operation", out var operation) ? operation.GetString()
            : "set";
        var kind = opText switch
        {
            "add" => EffectOperation.Add,
            "multiply" => EffectOperation.Multiply,
            _ => EffectOperation.Set,
        };
        var effect = new EffectDefinition(target, kind, new ConstantExpression());
        if (!parts.Router.CanApply(effect)) errors.Add(file, path + ".target", $"No handler accepts effect target '{target}' ({kind}).");
    }

    private static void Collect(DefinitionErrors errors, Action load)
    {
        try
        {
            load();
        }
        catch (DefinitionException ex)
        {
            if (ex.Errors.Count == 0) errors.Add(ex.File, ex.JsonPath, ex.Message);
            foreach (var error in ex.Errors) errors.Add(error.File, error.JsonPath, error.Message);
        }
    }

    private sealed class ConstantExpression : IExpression
    {
        public string Source => "0";

        public IReadOnlyList<string> Paths => [];

        public StateValue Evaluate(IStateQuery state) => 0.0;
    }
}
