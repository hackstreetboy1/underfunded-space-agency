using System.Globalization;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Scenario.Definitions;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Scenario;

/// <summary>Validates a <see cref="MissionDefinition"/> and compiles its expressions; every problem goes to <see cref="DefinitionErrors"/>.</summary>
internal sealed class MissionCompiler(string file, ExpressionCollector expressions, DefinitionErrors errors)
{
    private const double WeightTolerancePct = 1e-9;

    public Mission Compile(MissionDefinition definition)
    {
        CheckId(definition.Id, "$.id");
        if (string.IsNullOrWhiteSpace(definition.Title)) Error("$.title", "Title is empty.");
        if (definition.Era <= 0) Error("$.era", "Era must be a positive year.");
        CheckIdList(definition.Vehicles, "$.vehicles", "vehicle");
        if (definition.Crew.Count == 0) Error("$.crew", "A mission needs at least one crew member.");
        CheckUnique(definition.Crew.Select(c => c.Value).ToList(), "$.crew", "crew member");
        if (definition.Start.MetS < 0 || !double.IsFinite(definition.Start.MetS)) Error("$.start.metS", "Start MET must be a finite value >= 0.");

        var phases = CheckIdList(definition.Phases, "$.phases", "phase");
        CheckReference(phases, definition.Start.Phase, "$.start.phase", "phase");
        CheckReference(phases, definition.EndedPhase, "$.endedPhase", "phase");

        var eventIds = CheckIdList(definition.Events.Select(e => e.Id).ToList(), "$.events", "event");
        var events = CompileEvents(definition.Events);
        var conditions = CompileConditions(definition.Conditions ?? []);
        var transitions = CompileTransitions(definition.PhaseTransitions, phases, eventIds, definition.EndedPhase);
        var objectives = CompileObjectives(definition.Objectives);
        var objectiveIds = objectives.Select(o => o.Id).ToHashSet();
        var rules = CompileRules(definition.MissionRules ?? [], objectiveIds);
        var endConditions = CompileEndConditions(definition.EndConditions, eventIds);
        var timers = CompileTimers(definition.Timers ?? [], eventIds);

        return new Mission
        {
            Id = definition.Id,
            Title = definition.Title,
            Era = definition.Era,
            Vehicles = definition.Vehicles,
            Crew = definition.Crew,
            StartTime = MissionTime.FromSeconds(definition.Start.MetS),
            StartPhase = definition.Start.Phase,
            EndedPhase = definition.EndedPhase,
            Phases = definition.Phases,
            Files = definition.Files,
            Transitions = transitions,
            Events = events,
            Conditions = conditions,
            Objectives = objectives,
            Rules = rules,
            EndConditions = endConditions,
            Timers = timers,
            Locations = definition.Locations ?? [],
        };
    }

    private MissionEventSpec[] CompileEvents(IReadOnlyList<MissionEventDefinition> definitions)
    {
        var events = new MissionEventSpec[definitions.Count];
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var path = $"$.events[{i}]";
            if (definition.When is null && string.IsNullOrWhiteSpace(definition.Source))
                Error(path, $"Event '{definition.Id}' needs a 'when' expression or a 'source' naming who raises it.");
            var when = definition.When is null ? null : expressions.CompileCondition(file, path + ".when", definition.When);
            events[i] = new MissionEventSpec(definition.Id, when, definition.Source);
        }
        return events;
    }

    private NamedCondition[] CompileConditions(IReadOnlyList<NamedConditionDefinition> definitions)
    {
        CheckIdList(definitions.Select(c => c.Id).ToList(), "$.conditions", "condition");
        var conditions = new NamedCondition[definitions.Count];
        for (var i = 0; i < definitions.Count; i++)
        {
            var when = expressions.CompileCondition(file, $"$.conditions[{i}].when", definitions[i].When, isNamedCondition: true);
            conditions[i] = new NamedCondition(definitions[i].Id, when);
        }
        return conditions;
    }

    private PhaseTransition[] CompileTransitions(
        IReadOnlyList<PhaseTransitionDefinition> definitions,
        HashSet<string> phases,
        HashSet<string> eventIds,
        string endedPhase)
    {
        var transitions = new PhaseTransition[definitions.Count];
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var path = $"$.phaseTransitions[{i}]";
            if (definition.From.Count == 0) Error(path + ".from", "List at least one phase.");
            for (var f = 0; f < definition.From.Count; f++)
            {
                CheckReference(phases, definition.From[f], $"{path}.from[{f}]", "phase");
                if (definition.From[f] == definition.To) Error($"{path}.from[{f}]", "A transition cannot lead to the phase it starts from.");
            }
            CheckReference(phases, definition.To, path + ".to", "phase");
            if (definition.To == endedPhase) Error(path + ".to", "Only end conditions enter the ended phase; a transition cannot.");
            var trigger = CompileTrigger(definition.Event, definition.When, path, eventIds);
            transitions[i] = new PhaseTransition(definition.From, definition.To, trigger);
        }
        return transitions;
    }

    private Objective[] CompileObjectives(IReadOnlyList<ObjectiveDefinition> definitions)
    {
        if (definitions.Count == 0) Error("$.objectives", "A mission needs at least one objective.");
        CheckUnique(definitions.Select(o => o.Id.Value).ToList(), "$.objectives", "objective");

        var objectives = new Objective[definitions.Count];
        var primaryWeightPct = 0.0;
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var path = $"$.objectives[{i}]";
            if (string.IsNullOrWhiteSpace(definition.Title)) Error(path + ".title", "Title is empty.");
            if (definition.Kind == ObjectiveKind.Primary)
            {
                if (!(definition.WeightPct > 0)) Error(path + ".weightPct", "A primary objective needs a weight above 0.");
                primaryWeightPct += definition.WeightPct;
            }
            else if (definition.WeightPct != 0)
            {
                Error(path + ".weightPct", "Only primary objectives carry a weight.");
            }

            var complete = expressions.CompileCondition(file, path + ".complete", definition.Complete);
            var ruleDefinitions = definition.Feasibility ?? [];
            CheckUnique(ruleDefinitions.Select(r => r.Id).ToList(), path + ".feasibility", "feasibility rule");
            var rules = new FeasibilityRule[ruleDefinitions.Count];
            for (var r = 0; r < ruleDefinitions.Count; r++)
            {
                var rule = ruleDefinitions[r];
                var rulePath = $"{path}.feasibility[{r}]";
                CheckId(rule.Id, rulePath + ".id");
                if (rule.State is not (ObjectiveState.Impossible or ObjectiveState.UnacceptableRisk))
                    Error(rulePath + ".state", "A feasibility rule's state is 'impossible' or 'unacceptable_risk'.");
                if (rule.Latched && rule.State != ObjectiveState.UnacceptableRisk)
                    Error(rulePath + ".latched", "Only unacceptable_risk rules can be latched; impossible is always final.");
                var when = expressions.CompileCondition(file, rulePath + ".when", rule.When);
                var cause = CompileCause(rule.Cause, rule.AcceptableAlternative, rulePath);
                rules[r] = new FeasibilityRule(rule.Id, rule.State, when, cause, rule.Latched);
            }
            objectives[i] = new Objective(definition.Id, definition.Kind, definition.Title, definition.WeightPct, complete, rules);
        }

        if (definitions.Any(o => o.Kind == ObjectiveKind.Primary) && Math.Abs(primaryWeightPct - 100) > WeightTolerancePct)
            Error("$.objectives", string.Create(CultureInfo.InvariantCulture, $"Primary objective weights add up to {primaryWeightPct} %, not 100 %."));
        return objectives;
    }

    private MissionRule[] CompileRules(IReadOnlyList<MissionRuleDefinition> definitions, HashSet<ObjectiveId> objectiveIds)
    {
        CheckIdList(definitions.Select(r => r.Id).ToList(), "$.missionRules", "mission rule");
        var rules = new MissionRule[definitions.Count];
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var path = $"$.missionRules[{i}]";
            var when = expressions.CompileCondition(file, path + ".when", definition.When);
            var unacceptable = definition.Effect.ObjectivesUnacceptable ?? [];
            for (var o = 0; o < unacceptable.Count; o++)
            {
                if (!objectiveIds.Contains(unacceptable[o]))
                    Error($"{path}.effect.objectivesUnacceptable[{o}]", $"Unknown objective '{unacceptable[o]}'.");
            }
            if (definition.Effect.RequireReturn is null && unacceptable.Count == 0)
                Error(path + ".effect", "A mission rule needs 'requireReturn' or 'objectivesUnacceptable'.");
            var cause = CompileCause(definition.Cause, definition.AcceptableAlternative, path);
            rules[i] = new MissionRule(definition.Id, when, definition.Effect.RequireReturn, unacceptable, cause);
        }
        return rules;
    }

    private EndCondition[] CompileEndConditions(IReadOnlyList<EndConditionDefinition> definitions, HashSet<string> eventIds)
    {
        if (definitions.Count == 0) Error("$.endConditions", "A mission needs at least one end condition.");
        CheckIdList(definitions.Select(e => e.Id).ToList(), "$.endConditions", "end condition");
        var endConditions = new EndCondition[definitions.Count];
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var trigger = CompileTrigger(definition.Event, definition.When, $"$.endConditions[{i}]", eventIds);
            endConditions[i] = new EndCondition(definition.Id, trigger, definition.Outcome);
        }
        return endConditions;
    }

    private MissionTimer[] CompileTimers(IReadOnlyList<MissionTimerDefinition> definitions, HashSet<string> eventIds)
    {
        CheckIdList(definitions.Select(t => t.Id).ToList(), "$.timers", "timer");
        var timers = new MissionTimer[definitions.Count];
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var path = $"$.timers[{i}]";
            var runWhile = definition.RunWhile is null ? null : expressions.CompileCondition(file, path + ".runWhile", definition.RunWhile);
            var resetWhen = definition.ResetWhen is null ? null : expressions.CompileCondition(file, path + ".resetWhen", definition.ResetWhen);
            if (definition.StartEvent is { } start) CheckReference(eventIds, start, path + ".startEvent", "mission event");
            if (definition.StopEvent is { } stop) CheckReference(eventIds, stop, path + ".stopEvent", "mission event");
            if (definition.StartEvent is not null && definition.StartEvent == definition.StopEvent)
                Error(path + ".stopEvent", "A timer cannot start and stop on the same event.");

            long? durationMs = null;
            if (definition.DurationS is { } durationS)
            {
                // Timers step once a second, so a shorter deadline would expire on its first step.
                if (durationS >= 1 && double.IsFinite(durationS)) durationMs = MissionTime.FromSeconds(durationS).Milliseconds;
                else Error(path + ".durationS", "A deadline must be a finite number of seconds, at least 1 (the timer step).");
            }
            if (definition.ExpireEvent is { } expire)
            {
                CheckReference(eventIds, expire, path + ".expireEvent", "mission event");
                if (definition.DurationS is null) Error(path + ".expireEvent", "Only a timer with 'durationS' expires.");
                if (definition.ResetWhen is not null)
                    Error(path + ".expireEvent", "A reset re-arms the deadline, but a mission event fires only once; read mission.timer.<id>.expired instead.");
            }
            timers[i] = new MissionTimer(
                definition.Id, runWhile, resetWhen, definition.StartEvent, definition.StopEvent, durationMs, definition.ExpireEvent);
        }
        return timers;
    }

    private MissionTrigger CompileTrigger(string? eventId, string? when, string path, HashSet<string> eventIds)
    {
        if ((eventId is null) == (when is null))
        {
            Error(path, "Set exactly one of 'event' and 'when'.");
            return new MissionTrigger(eventId, null);
        }
        if (eventId is not null)
        {
            CheckReference(eventIds, eventId, path + ".event", "mission event");
            return new MissionTrigger(eventId, null);
        }
        return new MissionTrigger(null, expressions.CompileCondition(file, path + ".when", when));
    }

    private CausePolicy CompileCause(FeasibilityCause cause, string? alternative, string path)
    {
        if (alternative is null)
        {
            // GDD 10.3: a loss is player_avoidable only when an acceptable alternative existed; "always" is written "true".
            if (cause == FeasibilityCause.PlayerAvoidable)
                Error(path + ".acceptableAlternative", "Cause 'player_avoidable' needs an acceptable alternative (write \"true\" for always).");
            return new CausePolicy(cause, null);
        }
        if (cause != FeasibilityCause.PlayerAvoidable)
            Error(path + ".acceptableAlternative", "An acceptable alternative only applies to cause 'player_avoidable'.");
        return new CausePolicy(cause, expressions.CompileCondition(file, path + ".acceptableAlternative", alternative));
    }

    private HashSet<string> CheckIdList(IReadOnlyList<string> ids, string path, string kind)
    {
        for (var i = 0; i < ids.Count; i++) CheckId(ids[i], $"{path}[{i}]");
        return CheckUnique(ids, path, kind);
    }

    private HashSet<string> CheckUnique(IReadOnlyList<string> ids, string path, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Count; i++)
        {
            if (!seen.Add(ids[i])) Error($"{path}[{i}]", $"Duplicate {kind} id '{ids[i]}'.");
        }
        return seen;
    }

    private void CheckReference(HashSet<string> known, string id, string path, string kind)
    {
        if (!known.Contains(id)) Error(path, $"Unknown {kind} '{id}'.");
    }

    private void CheckId(string id, string path)
    {
        if (string.IsNullOrEmpty(id) || id.Contains('/', StringComparison.Ordinal) || id.Contains('.', StringComparison.Ordinal))
        {
            Error(path, $"'{id}' is not a snake_case id.");
            return;
        }
        try
        {
            IdRules.Validate(id, "Id");
        }
        catch (ArgumentException ex)
        {
            Error(path, ex.Message);
        }
    }

    private void Error(string jsonPath, string message) => errors.Add(file, jsonPath, message);
}
