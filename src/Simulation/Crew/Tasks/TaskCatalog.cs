using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>Validated crew tasks from <c>catalog/tasks.json</c>, sorted by id.</summary>
public sealed class TaskCatalog
{
    public const string DefaultPath = "catalog/tasks.json";
    public const string SchemaTag = "tasks/1";

    private readonly TaskDefinition[] _tasks;
    private readonly Dictionary<TaskId, TaskDefinition> _byId;
    private readonly Dictionary<TaskId, int> _sourceIndex;
    private readonly string _file;

    private TaskCatalog(TaskDefinition[] tasks, Dictionary<TaskId, int> sourceIndex, string file)
    {
        _tasks = tasks;
        _byId = tasks.ToDictionary(t => t.Id);
        _sourceIndex = sourceIndex;
        _file = file;
    }

    public IReadOnlyList<TaskDefinition> Tasks => _tasks;

    public TaskDefinition this[TaskId id] =>
        TryGet(id, out var task) ? task : throw new KeyNotFoundException($"Unknown task '{id}'.");

    public bool TryGet(TaskId id, [MaybeNullWhen(false)] out TaskDefinition task)
    {
        return _byId.TryGetValue(id, out task);
    }

    public static TaskCatalog Load(DefinitionLoader loader, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return Create(loader.Load<TaskCatalogDefinition>(path, SchemaTag), path);
    }

    /// <summary>Structural validation: ids, ranges, parameters and placeholders.</summary>
    /// <exception cref="DefinitionException">Every problem in the file, reported together.</exception>
    public static TaskCatalog Create(TaskCatalogDefinition definition, string file)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new DefinitionErrors();
        var sourceIndex = new Dictionary<TaskId, int>();
        for (var i = 0; i < definition.Tasks.Count; i++)
        {
            var task = definition.Tasks[i];
            var path = TaskPath(i);
            if (!sourceIndex.TryAdd(task.Id, i)) errors.Add(file, path + ".id", $"Duplicate task id '{task.Id}'.");
            ValidateTask(task, file, path, errors);
        }
        errors.ThrowIfAny();

        var tasks = definition.Tasks.ToArray();
        Array.Sort(tasks, (a, b) => a.Id.CompareTo(b.Id));
        return new TaskCatalog(tasks, sourceIndex, file);
    }

    /// <summary>
    /// Binds every task with sample arguments (each choice and boolean value; numbers at their minimum)
    /// and checks that expressions compile and that facts, components, parts, read paths and effect
    /// targets exist. Read paths and targets are not checked for tasks with identifier parameters,
    /// whose paths depend on free text.
    /// </summary>
    public void ValidateBindings(TaskBinder binder, TaskReferenceChecks checks, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var task in _tasks)
        {
            var path = TaskPath(_sourceIndex[task.Id]);
            if (checks.Locations is { } locations) CheckLocations(task, locations, path, errors);
            if (checks.IsKnownPart is { } isKnownPart)
            {
                for (var p = 0; p < task.RequiredParts.Count; p++)
                {
                    if (!isKnownPart(task.RequiredParts[p].Part))
                        errors.Add(_file, Indexed(path + ".requiredParts", p), $"Unknown part '{task.RequiredParts[p].Part}'.");
                }
            }

            var freeText = task.Parameters.Any(p => p.Type == TaskParameterType.Identifier);
            foreach (var arguments in SampleArguments(task))
            {
                var label = arguments.Count == 0 ? "" : $" (with {arguments})";
                TaskBindResult result;
                try
                {
                    result = binder.Bind(task, arguments);
                }
                catch (ExpressionException ex)
                {
                    errors.Add(_file, path, ex.Message + label);
                    continue;
                }
                catch (ArgumentException ex)
                {
                    errors.Add(_file, path, ex.Message + label);
                    continue;
                }

                if (!result.IsSuccess)
                {
                    errors.Add(_file, path, $"Sample arguments rejected: {result.ErrorReason} '{result.Parameter}'{label}.");
                    continue;
                }
                CheckReferences(result.Task, checks, freeText, path, label, errors);
            }
        }
    }

    /// <summary>Every combination of sample argument values for <paramref name="task"/>.</summary>
    public static IReadOnlyList<TaskArguments> SampleArguments(TaskDefinition task)
    {
        ArgumentNullException.ThrowIfNull(task);
        IEnumerable<KeyValuePair<string, StateValue>[]> combinations = [[]];
        foreach (var parameter in task.Parameters)
        {
            StateValue[] samples = parameter.Type switch
            {
                TaskParameterType.Choice => parameter.Values.Select(v => StateValue.Of(v)).ToArray(),
                TaskParameterType.Boolean => [true, false],
                TaskParameterType.Number => [parameter.Min ?? parameter.Max ?? 0.0],
                _ => ["sample"],
            };
            var name = parameter.Name;
            combinations = combinations
                .SelectMany(prefix => samples.Select(sample => prefix.Append(new KeyValuePair<string, StateValue>(name, sample)).ToArray()))
                .ToArray();
        }
        return combinations.Select(c => new TaskArguments(c)).ToArray();
    }

    private void CheckReferences(BoundTask bound, TaskReferenceChecks checks, bool freeText, string path, string label, DefinitionErrors errors)
    {
        foreach (var observation in bound.Observations)
        {
            if (checks.IsKnownFact is { } isKnownFact && !isKnownFact(observation.Fact))
                errors.Add(_file, path + ".observations", $"Unknown fact '{observation.Fact}'{label}.");
            if (checks.IsKnownComponent is not { } isKnownComponent) continue;
            foreach (var component in observation.Scope)
            {
                if (!isKnownComponent(component))
                    errors.Add(_file, path + ".observations", $"Unknown component '{component}'{label}.");
            }
        }

        if (freeText) return;
        if (checks.State is { } state)
        {
            foreach (var precondition in bound.Preconditions)
            {
                foreach (var unknown in precondition.Condition.UnknownPaths(state))
                    errors.Add(_file, path + ".preconditions", $"Unknown state path '{unknown}'{label}.");
            }
            CheckEffectPaths(bound.OnSuccess, state, path + ".onSuccess", label, errors);
            CheckEffectPaths(bound.OnFailure, state, path + ".onFailure", label, errors);
            CheckEffectPaths(bound.OnStop, state, path + ".onStop", label, errors);
        }
        if (checks.CanApplyEffect is { } canApply)
        {
            CheckEffectTargets(bound.OnSuccess, canApply, path + ".onSuccess", label, errors);
            CheckEffectTargets(bound.OnFailure, canApply, path + ".onFailure", label, errors);
            CheckEffectTargets(bound.OnStop, canApply, path + ".onStop", label, errors);
        }
    }

    private void CheckLocations(TaskDefinition task, CrewLocations locations, string path, DefinitionErrors errors)
    {
        var index = locations.IndexOf(task.LocationId);
        if (!locations.IsTaskLocation(task.LocationId))
            errors.Add(_file, path + ".location", $"Unknown crew location '{task.LocationId}'.");
        else if (task.Kind == TaskKind.Eva && (index < 0 || !locations[index].IsSuited))
            errors.Add(_file, path + ".location", "EVA tasks take place at a suited location.");
        for (var i = 0; i < task.RequiredLocations.Count; i++)
        {
            if (!locations.Contains(task.RequiredLocations[i]))
                errors.Add(_file, Indexed(path + ".requiredLocations", i), $"Unknown crew location '{task.RequiredLocations[i]}'.");
        }
        if (task.MovesTo is { } movesTo && !locations.Contains(movesTo))
            errors.Add(_file, path + ".movesTo", $"Unknown crew location '{movesTo}'.");
    }

    private void CheckEffectPaths(IReadOnlyList<EffectDefinition> effects, IStateQuery state, string path, string label, DefinitionErrors errors)
    {
        foreach (var effect in effects)
        {
            foreach (var unknown in effect.Value.UnknownPaths(state))
                errors.Add(_file, path, $"Unknown state path '{unknown}'{label}.");
        }
    }

    private void CheckEffectTargets(IReadOnlyList<EffectDefinition> effects, Func<EffectDefinition, bool> canApply, string path, string label, DefinitionErrors errors)
    {
        foreach (var effect in effects)
        {
            if (!canApply(effect)) errors.Add(_file, path, $"No effect handler accepts '{effect.Operation}' on '{effect.Target}'{label}.");
        }
    }

    private static void ValidateTask(TaskDefinition task, string file, string path, DefinitionErrors errors)
    {
        if (string.IsNullOrWhiteSpace(task.Name)) errors.Add(file, path + ".name", "Name is empty.");
        if (task.Skills.Count == 0) errors.Add(file, path + ".skills", "At least one skill domain is required.");
        if (task.Skills.Distinct().Count() != task.Skills.Count) errors.Add(file, path + ".skills", "Skill domains repeat.");
        if (!(task.BaseDurationS > 0 && double.IsFinite(task.BaseDurationS))) errors.Add(file, path + ".baseDurationS", "Duration must be positive.");
        if (task.Complexity is < 1 or > 5) errors.Add(file, path + ".complexity", "Complexity is 1-5.");
        if (!(task.EquipmentFactor > 0 && double.IsFinite(task.EquipmentFactor))) errors.Add(file, path + ".equipmentFactor", "Equipment factor must be positive.");
        if (task.LocationId.Length == 0) errors.Add(file, path + ".location", "Location is required.");
        else if (!CrewLocations.IsName(task.LocationId)) errors.Add(file, path + ".location", $"'{task.LocationId}' is not a snake_case location id.");
        else if (task.Kind == TaskKind.Eva && task.LocationId != CrewLocations.EvaId) errors.Add(file, path + ".location", "EVA tasks take place at location 'eva'.");
        for (var i = 0; i < task.RequiredLocations.Count; i++)
        {
            var required = task.RequiredLocations[i];
            if (!CrewLocations.IsName(required) || required == CrewLocations.CurrentId)
                errors.Add(file, Indexed(path + ".requiredLocations", i), $"'{required}' is not a location id.");
        }
        if (task.MovesTo is { } movesTo && (!CrewLocations.IsName(movesTo) || movesTo == CrewLocations.CurrentId))
            errors.Add(file, path + ".movesTo", $"'{movesTo}' is not a location id.");

        var names = ValidateParameters(task, file, path, errors);

        for (var i = 0; i < task.RequiredParts.Count; i++)
        {
            if (task.RequiredParts[i].Count < 1) errors.Add(file, Indexed(path + ".requiredParts", i) + ".count", "Count must be at least 1.");
        }
        for (var i = 0; i < task.Preconditions.Count; i++)
        {
            var precondition = task.Preconditions[i];
            var itemPath = Indexed(path + ".preconditions", i);
            if (string.IsNullOrWhiteSpace(precondition.Condition)) errors.Add(file, itemPath + ".condition", "Condition is empty.");
            if (!TaskTemplate.IsName(precondition.Reason)) errors.Add(file, itemPath + ".reason", "Reason codes are snake_case.");
            CheckTemplate(precondition.Condition, names, file, itemPath + ".condition", errors);
        }
        ValidateEffects(task.OnSuccess, names, file, path + ".onSuccess", errors);
        ValidateEffects(task.OnFailure, names, file, path + ".onFailure", errors);
        ValidateEffects(task.OnStop, names, file, path + ".onStop", errors);
        for (var i = 0; i < task.Observations.Count; i++)
        {
            var observation = task.Observations[i];
            var itemPath = Indexed(path + ".observations", i);
            CheckTemplate(observation.Fact, names, file, itemPath + ".fact", errors);
            for (var s = 0; s < observation.Scope.Count; s++) CheckTemplate(observation.Scope[s], names, file, Indexed(itemPath + ".scope", s), errors);
        }
    }

    /// <returns>Every placeholder name the task may use.</returns>
    private static HashSet<string> ValidateParameters(TaskDefinition task, string file, string path, DefinitionErrors errors)
    {
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        var derivedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < task.Parameters.Count; i++)
        {
            var parameter = task.Parameters[i];
            var itemPath = Indexed(path + ".parameters", i);
            if (!TaskTemplate.IsName(parameter.Name)) errors.Add(file, itemPath + ".name", "Parameter names are snake_case.");
            if (!parameterNames.Add(parameter.Name)) errors.Add(file, itemPath + ".name", $"Parameter '{parameter.Name}' repeats.");

            var isChoice = parameter.Type == TaskParameterType.Choice;
            if (isChoice && parameter.Values.Count == 0) errors.Add(file, itemPath + ".values", "A choice needs values.");
            if (!isChoice && (parameter.Values.Count > 0 || parameter.Derived.Count > 0))
                errors.Add(file, itemPath, "Only choice parameters have values and derived placeholders.");
            if (isChoice && parameter.Values.Distinct(StringComparer.Ordinal).Count() != parameter.Values.Count)
                errors.Add(file, itemPath + ".values", "Values repeat.");
            if (parameter.Type != TaskParameterType.Number && (parameter.Min is not null || parameter.Max is not null))
                errors.Add(file, itemPath, "Only number parameters have min and max.");
            if (parameter.Min is { } min && parameter.Max is { } max && !(min <= max))
                errors.Add(file, itemPath, "min must not exceed max.");

            if (parameter.Derived.Count == 0) continue;
            var expected = parameter.Derived.Values.First().Keys.Order(StringComparer.Ordinal).ToArray();
            foreach (var value in parameter.Values)
            {
                if (!parameter.Derived.TryGetValue(value, out var derived))
                {
                    errors.Add(file, itemPath + ".derived", $"Value '{value}' has no derived placeholders.");
                    continue;
                }
                if (!derived.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal))
                    errors.Add(file, $"{itemPath}.derived.{value}", "Every value must define the same derived names.");
            }
            foreach (var key in parameter.Derived.Keys.Order(StringComparer.Ordinal))
            {
                if (!parameter.Values.Contains(key, StringComparer.Ordinal)) errors.Add(file, $"{itemPath}.derived.{key}", $"'{key}' is not a value.");
            }
            foreach (var name in expected)
            {
                if (!TaskTemplate.IsName(name)) errors.Add(file, itemPath + ".derived", $"Derived name '{name}' is not snake_case.");
                derivedNames.Add(name);
            }
        }

        foreach (var name in derivedNames.Order(StringComparer.Ordinal))
        {
            if (!parameterNames.Add(name)) errors.Add(file, path + ".parameters", $"Derived name '{name}' clashes with another placeholder.");
        }
        return parameterNames;
    }

    private static void ValidateEffects(IReadOnlyList<TaskEffectDefinition> effects, HashSet<string> names, string file, string path, DefinitionErrors errors)
    {
        for (var i = 0; i < effects.Count; i++)
        {
            var itemPath = Indexed(path, i);
            if (string.IsNullOrWhiteSpace(effects[i].Target)) errors.Add(file, itemPath + ".target", "Target is empty.");
            if (string.IsNullOrWhiteSpace(effects[i].Value)) errors.Add(file, itemPath + ".value", "Value is empty.");
            CheckTemplate(effects[i].Target, names, file, itemPath + ".target", errors);
            CheckTemplate(effects[i].Value, names, file, itemPath + ".value", errors);
        }
    }

    private static void CheckTemplate(string template, HashSet<string> names, string file, string path, DefinitionErrors errors)
    {
        var used = new List<string>();
        if (!TaskTemplate.TryGetPlaceholders(template, used))
        {
            errors.Add(file, path, $"Malformed placeholder in \"{template}\".");
            return;
        }
        foreach (var name in used)
        {
            if (!names.Contains(name)) errors.Add(file, path, $"Placeholder '{{{name}}}' is not a parameter.");
        }
    }

    private static string TaskPath(int index) => Indexed("$.tasks", index);

    private static string Indexed(string path, int index) => string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");
}
