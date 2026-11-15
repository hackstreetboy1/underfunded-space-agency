using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>Validates one action definition and compiles its expressions, collecting every error.</summary>
internal sealed class ActionCompiler(IExpressionCompiler compiler, ActionCatalogChecks checks, string file, DefinitionErrors errors)
{
    private static readonly IReadOnlyList<string> BooleanDomain = ["false", "true"];

    public CatalogAction? Compile(ActionDefinition action, string path)
    {
        var errorsBefore = errors.All.Count;
        if (string.IsNullOrWhiteSpace(action.Description)) Error(path + ".description", "Description is required.");
        CheckPhases(action, path);
        var domains = CheckParameters(action, path);
        CheckKind(action, path);

        var preconditions = CompilePreconditions(action, domains, path);
        var impactRules = CompileImpactRules(action, path);
        var effects = CompileEffects(action, domains, path);
        var affects = CompileAffects(action, domains, path);

        return errors.All.Count == errorsBefore
            ? new CatalogAction(action, preconditions, impactRules, effects, affects)
            : null;
    }

    private void CheckPhases(ActionDefinition action, string path)
    {
        if (action.Phases.Count == 0) Error(path + ".phases", "At least one phase is required.");
        for (var i = 0; i < action.Phases.Count; i++)
        {
            if (checks.KnownPhases is { } known && !known.Contains(action.Phases[i]))
                Error($"{path}.phases[{i}]", $"Unknown phase '{action.Phases[i]}'.");
        }
    }

    /// <summary>Returns the placeholder domains: enum values and booleans by parameter name.</summary>
    private Dictionary<string, IReadOnlyList<string>> CheckParameters(ActionDefinition action, string path)
    {
        var domains = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var parameters = action.Parameters ?? [];
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var at = $"{path}.parameters[{i}]";
            if (!ArgumentTemplate.IsParameterName(parameter.Name)) Error(at + ".name", $"Parameter name '{parameter.Name}' must be snake_case.");
            if (!names.Add(parameter.Name)) Error(at + ".name", $"Duplicate parameter '{parameter.Name}'.");

            var isNumeric = parameter.Type is ParameterType.Number or ParameterType.Integer;
            if (!isNumeric && (parameter.Min is not null || parameter.Max is not null))
                Error(at, "min/max apply only to number and integer parameters.");
            if (isNumeric && parameter.Min is { } min && parameter.Max is { } max && !(min <= max))
                Error(at, $"min {min} is greater than max {max}.");

            if (parameter.Type == ParameterType.Enum)
            {
                var values = parameter.Values ?? [];
                if (values.Count == 0) Error(at + ".values", "An enum parameter needs values.");
                if (values.Distinct(StringComparer.Ordinal).Count() != values.Count) Error(at + ".values", "Enum values must be distinct.");
                domains[parameter.Name] = values;
            }
            else if (parameter.Values is not null)
            {
                Error(at + ".values", "Only enum parameters take values.");
            }

            if (parameter.Type == ParameterType.Boolean) domains[parameter.Name] = BooleanDomain;
        }
        return domains;
    }

    private void CheckKind(ActionDefinition action, string path)
    {
        var kind = action.Kind;
        if (kind == ActionKind.CrewTask)
        {
            if (action.TaskId is not { } task) Error(path + ".taskId", "A crew_task action needs a taskId.");
            else if (checks.KnownTasks is { } tasks && !tasks.Contains(task)) Error(path + ".taskId", $"Unknown task '{task}'.");
        }
        else if (action.TaskId is not null)
        {
            Error(path + ".taskId", "Only crew_task actions take a taskId.");
        }

        if (kind == ActionKind.Remote && (action.Effects is null || action.Effects.Count == 0))
            Error(path + ".effects", "A remote action needs effects.");
        if (kind != ActionKind.Remote && action.Effects is not null) Error(path + ".effects", "Only remote actions take effects.");

        if (kind == ActionKind.Meta && action.Meta is null) Error(path + ".meta", "A meta action needs a meta command.");
        if (kind != ActionKind.Meta && action.Meta is not null) Error(path + ".meta", "Only meta actions take a meta command.");
        if (kind != ActionKind.Meta && action.UsesCrew) Error(path + ".usesCrew", "usesCrew applies only to meta actions.");
        if (action.Meta == MetaCommandKind.RegisterRepairDesign)
        {
            foreach (var name in (ReadOnlySpan<string>)[RepairDesignArgs.Assembly, RepairDesignArgs.Design])
            {
                if (ActionParameters.Find(action.Parameters, name) is not { Type: ParameterType.String })
                    Error(path + ".parameters", $"register_repair_design needs a string parameter '{name}'.");
            }
        }

        if (kind == ActionKind.GlanceRead) CheckGlance(action, path + ".glance");
        else if (action.Glance is not null) Error(path + ".glance", "Only glance_read actions take a glance.");
    }

    private void CheckGlance(ActionDefinition action, string path)
    {
        if (action.Glance is not { } glance)
        {
            Error(path, "A glance_read action needs a glance.");
            return;
        }
        if (!(glance.DelayS > 0) || double.IsInfinity(glance.DelayS)) Error(path + ".delayS", "delayS must be positive.");

        var hasFacts = glance.Facts is { Count: > 0 };
        if (hasFacts == (glance.FactParam is not null)) Error(path, "Give exactly one of facts and factParam.");

        foreach (var fact in glance.Facts ?? []) CheckFact(fact, path + ".facts");
        if (glance.FactParam is not { } param) return;

        var parameter = ActionParameters.Find(action.Parameters, param);
        if (parameter is not { Type: ParameterType.Enum })
        {
            Error(path + ".factParam", $"factParam '{param}' must name an enum parameter.");
            return;
        }
        foreach (var value in parameter.Values ?? [])
        {
            if (IdParsing.TryCreate(value, v => new FactId(v), out var fact)) CheckFact(fact, path + ".factParam");
            else Error(path + ".factParam", $"'{value}' is not a valid fact id.");
        }
    }

    private void CheckFact(FactId fact, string path)
    {
        if (checks.KnownFacts is { } facts && !facts.Contains(fact)) Error(path, $"Unknown fact '{fact}'.");
    }

    private CompiledPrecondition[] CompilePreconditions(ActionDefinition action, Dictionary<string, IReadOnlyList<string>> domains, string path)
    {
        var preconditions = action.Preconditions ?? [];
        var compiled = new List<CompiledPrecondition>(preconditions.Count);
        for (var i = 0; i < preconditions.Count; i++)
        {
            var at = $"{path}.preconditions[{i}]";
            var precondition = preconditions[i];
            if (!ArgumentTemplate.IsParameterName(precondition.Reason))
                Error(at + ".reason", $"Reason code '{precondition.Reason}' must be snake_case.");
            if (CompileExpressions(precondition.Require, domains, at + ".require") is { } expressions)
                compiled.Add(new CompiledPrecondition(expressions.Template, precondition.Reason, expressions.Compiled));
        }
        return [.. compiled];
    }

    private CompiledImpactRule[] CompileImpactRules(ActionDefinition action, string path)
    {
        var rules = action.ImpactRules ?? [];
        var compiled = new List<CompiledImpactRule>(rules.Count);
        var hasActionText = action.EffectTextKeys is { Count: > 0 };
        if (action.Impact >= ActionImpact.High && !hasActionText)
            Error(path + ".effectTextKeys", "High and irreversible actions need effect text keys for the confirmation prompt.");

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var at = $"{path}.impactRules[{i}]";
            if (rule.Impact <= action.Impact) Error(at + ".impact", "A rule must raise the action's impact.");
            if (!hasActionText && rule.EffectTextKeys is not { Count: > 0 })
                Error(at + ".effectTextKeys", "A rule raising impact needs effect text keys for the confirmation prompt.");
            if (rule.Match.Count == 0) Error(at + ".match", "A rule needs at least one parameter to match.");

            var conditions = new List<KeyValuePair<string, string[]>>();
            foreach (var name in rule.Match.Keys.Order(StringComparer.Ordinal))
            {
                var at2 = $"{at}.match.{name}";
                var patterns = rule.Match[name];
                if (ActionParameters.Find(action.Parameters, name) is not { } parameter)
                {
                    Error(at2, $"Unknown parameter '{name}'.");
                    continue;
                }
                if (patterns.Count == 0) Error(at2, "At least one pattern is required.");
                var domain = parameter.Type switch
                {
                    ParameterType.Enum => parameter.Values ?? [],
                    ParameterType.Boolean => BooleanDomain,
                    _ => null,
                };
                foreach (var pattern in patterns)
                {
                    if (domain is not null && !domain.Any(value => CompiledImpactRule.MatchesAny([pattern], value)))
                        Error(at2, $"Pattern '{pattern}' matches no value of '{name}'.");
                }
                conditions.Add(new(name, [.. patterns]));
            }
            compiled.Add(new CompiledImpactRule([.. conditions], rule.Impact, rule.EffectTextKeys ?? []));
        }
        return [.. compiled];
    }

    private CompiledEffect[] CompileEffects(ActionDefinition action, Dictionary<string, IReadOnlyList<string>> domains, string path)
    {
        var effects = action.Effects ?? [];
        var compiled = new List<CompiledEffect>(effects.Count);
        for (var i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            var at = $"{path}.effects[{i}]";
            var target = ParseTemplate(effect.Target, domains, at + ".target");
            if (target is not null && checks.State is { } state)
            {
                foreach (var expanded in target.Expand(domains))
                {
                    if (!state.IsKnownPath(expanded)) Error(at + ".target", $"Unknown state path '{expanded}'.");
                }
            }

            if ((effect.Value is null) == (effect.Arg is null))
            {
                Error(at, "Give exactly one of value and arg.");
                continue;
            }

            if (effect.Arg is { } arg)
            {
                if (ActionParameters.Find(action.Parameters, arg) is null) Error(at + ".arg", $"Unknown parameter '{arg}'.");
                else if (target is not null) compiled.Add(new CompiledEffect(target, effect.Op, arg, null, null));
                continue;
            }

            if (CompileExpressions(effect.Value!, domains, at + ".value") is { } value && target is not null)
                compiled.Add(new CompiledEffect(target, effect.Op, null, value.Template, value.Compiled));
        }
        return [.. compiled];
    }

    private ArgumentTemplate[] CompileAffects(ActionDefinition action, Dictionary<string, IReadOnlyList<string>> domains, string path)
    {
        var affects = action.Affects ?? [];
        var compiled = new List<ArgumentTemplate>(affects.Count);
        for (var i = 0; i < affects.Count; i++)
        {
            var at = $"{path}.affects[{i}]";
            if (ParseTemplate(affects[i], domains, at) is not { } template) continue;
            foreach (var expanded in template.Expand(domains))
            {
                if (!IdParsing.TryCreate(expanded, v => new ComponentId(v), out var component)) Error(at, $"'{expanded}' is not a valid component id.");
                else if (checks.KnownComponents is { } known && !known.Contains(component)) Error(at, $"Unknown component '{expanded}'.");
            }
            compiled.Add(template);
        }
        return [.. compiled];
    }

    private (ArgumentTemplate Template, IReadOnlyDictionary<string, IExpression> Compiled)? CompileExpressions(
        string source, Dictionary<string, IReadOnlyList<string>> domains, string path)
    {
        if (ParseTemplate(source, domains, path) is not { } template) return null;

        var compiled = new Dictionary<string, IExpression>(StringComparer.Ordinal);
        foreach (var expanded in template.Expand(domains))
        {
            IExpression expression;
            try
            {
                expression = compiler.Compile(expanded);
            }
            catch (ExpressionException ex)
            {
                Error(path, ex.Message);
                continue;
            }

            compiled[expanded] = expression;
            if (checks.State is not { } state) continue;
            foreach (var statePath in expression.Paths)
            {
                if (!state.IsKnownPath(statePath)) Error(path, $"Unknown state path '{statePath}' in \"{expanded}\".");
            }
        }
        return (template, compiled);
    }

    private ArgumentTemplate? ParseTemplate(string source, Dictionary<string, IReadOnlyList<string>> domains, string path)
    {
        ArgumentTemplate template;
        try
        {
            template = ArgumentTemplate.Parse(source);
        }
        catch (FormatException ex)
        {
            Error(path, ex.Message);
            return null;
        }

        var valid = true;
        foreach (var name in template.Placeholders)
        {
            if (domains.ContainsKey(name)) continue;
            Error(path, $"Placeholder '{{{name}}}' must name an enum or boolean parameter.");
            valid = false;
        }
        return valid ? template : null;
    }

    private void Error(string path, string message) => errors.Add(file, path, message);
}
