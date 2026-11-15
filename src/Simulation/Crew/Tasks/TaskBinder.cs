using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>
/// Validates arguments against a task's parameters, substitutes placeholders and compiles the resulting
/// expressions (cached by source). Part requirements become a <c>missing_part</c> precondition and a
/// consumption effect appended to the success effects; tools are checked but never consumed.
/// </summary>
/// <param name="isTool">Tells tools apart from consumable parts (<c>PartCatalog.IsTool</c>); required, so a tool is never consumed by default.</param>
public sealed class TaskBinder(IExpressionCompiler compiler, Func<PartId, bool> isTool)
{
    private readonly Dictionary<string, IExpression> _compiled = new(StringComparer.Ordinal);

    /// <returns>The bound task, or a reason code for a missing, unknown or invalid argument.</returns>
    /// <exception cref="ExpressionException">A template compiles to invalid source (a data error caught at load).</exception>
    /// <exception cref="ArgumentException">A template produces an invalid fact or component id (a data error).</exception>
    public TaskBindResult Bind(TaskDefinition task, TaskArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var entry in arguments.Entries)
        {
            if (FindParameter(task, entry.Key) is null) return TaskBindResult.Failure(TaskBindResult.UnknownParameter, entry.Key);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in task.Parameters)
        {
            if (!arguments.TryGet(parameter.Name, out var value)) return TaskBindResult.Failure(TaskBindResult.MissingParameter, parameter.Name);
            if (!IsValid(parameter, value)) return TaskBindResult.Failure(TaskBindResult.InvalidParameter, parameter.Name);
            var text = TaskTemplate.Format(value);
            values[parameter.Name] = text;
            if (parameter.Derived.TryGetValue(text, out var derived))
            {
                foreach (var pair in derived) values[pair.Key] = pair.Value;
            }
        }

        var preconditions = new List<BoundPrecondition>(task.RequiredParts.Count + task.Preconditions.Count);
        foreach (var part in task.RequiredParts) preconditions.Add(PartExpressions.Available(part));
        foreach (var precondition in task.Preconditions)
        {
            preconditions.Add(new BoundPrecondition(Compile(TaskTemplate.Substitute(precondition.Condition, values)), precondition.Reason));
        }

        var onSuccess = BindEffects(task.OnSuccess, values, task.RequiredParts.Count);
        foreach (var part in task.RequiredParts)
        {
            if (!isTool(part.Part)) onSuccess.Add(PartExpressions.Consume(part));
        }

        var observations = new TaskObservation[task.Observations.Count];
        for (var i = 0; i < observations.Length; i++)
        {
            var observation = task.Observations[i];
            var scope = new ComponentId[observation.Scope.Count];
            for (var s = 0; s < scope.Length; s++) scope[s] = new ComponentId(TaskTemplate.Substitute(observation.Scope[s], values));
            observations[i] = new TaskObservation(new FactId(TaskTemplate.Substitute(observation.Fact, values)), observation.Method, scope);
        }

        return TaskBindResult.Success(new BoundTask(
            task,
            arguments,
            preconditions,
            onSuccess,
            BindEffects(task.OnFailure, values, 0),
            BindEffects(task.OnStop, values, 0),
            observations));
    }

    public static bool IsValid(TaskParameterDefinition parameter, StateValue value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        switch (parameter.Type)
        {
            case TaskParameterType.Choice:
                if (value.Kind != StateValueKind.Text) return false;
                foreach (var allowed in parameter.Values)
                {
                    if (allowed == value.AsText()) return true;
                }
                return false;
            case TaskParameterType.Number:
                if (value.Kind != StateValueKind.Number) return false;
                var number = value.AsNumber();
                return double.IsFinite(number)
                       && !(parameter.Min is { } min && number < min)
                       && !(parameter.Max is { } max && number > max);
            case TaskParameterType.Boolean:
                return value.Kind == StateValueKind.Bool;
            case TaskParameterType.Identifier:
                return value.Kind == StateValueKind.Text && IsIdentifier(value.AsText());
            default:
                return false;
        }
    }

    private List<EffectDefinition> BindEffects(IReadOnlyList<TaskEffectDefinition> effects, IReadOnlyDictionary<string, string> values, int extraCapacity)
    {
        var bound = new List<EffectDefinition>(effects.Count + extraCapacity);
        foreach (var effect in effects)
        {
            bound.Add(new EffectDefinition(
                TaskTemplate.Substitute(effect.Target, values),
                effect.Operation,
                Compile(TaskTemplate.Substitute(effect.Value, values))));
        }
        return bound;
    }

    private IExpression Compile(string source)
    {
        if (_compiled.TryGetValue(source, out var expression)) return expression;
        expression = compiler.Compile(source);
        _compiled.Add(source, expression);
        return expression;
    }

    private static TaskParameterDefinition? FindParameter(TaskDefinition task, string name)
    {
        foreach (var parameter in task.Parameters)
        {
            if (parameter.Name == name) return parameter;
        }
        return null;
    }

    // Identifiers are substituted into state paths, so separators ('.', '/') are refused.
    private static bool IsIdentifier(string text)
    {
        if (text.Length == 0) return false;
        foreach (var c in text)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')) return false;
        }
        return true;
    }
}
