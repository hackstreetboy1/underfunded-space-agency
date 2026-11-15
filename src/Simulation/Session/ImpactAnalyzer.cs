using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Session;

/// <summary>
/// Predicts objective changes for the confirmation prompt from the mission rules: when a command writes a path that an
/// inactive rule reads (directly or through a named condition), every objective that rule makes unacceptable and that is
/// still achievable is listed as achievable → unacceptable_risk.
/// </summary>
internal sealed class ImpactAnalyzer : IImpactAnalyzer
{
    private const string Achievable = "achievable";
    private const string UnacceptableRisk = "unacceptable_risk";

    private readonly Mission _mission;
    private readonly IStateQuery _state;
    private readonly TaskCatalog _tasks;
    private readonly TaskBinder _binder;
    private readonly HashSet<string>[] _rulePaths;

    public ImpactAnalyzer(Mission mission, IStateQuery state, TaskCatalog tasks, TaskBinder binder)
    {
        _mission = mission;
        _state = state;
        _tasks = tasks;
        _binder = binder;
        _rulePaths = new HashSet<string>[mission.Rules.Count];
        for (var r = 0; r < _rulePaths.Length; r++)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in mission.Rules[r].When.Paths)
            {
                paths.Add(path);
                foreach (var condition in mission.Conditions)
                {
                    if (path == $"{StatePaths.Mission}condition.{condition.Id}") paths.UnionWith(condition.When.Paths);
                }
            }
            _rulePaths[r] = paths;
        }
    }

    public IReadOnlyList<ObjectiveFeasibilityChange> PredictObjectiveChanges(ResolvedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var targets = Targets(command);
        if (targets.Count == 0) return [];

        var changes = new SortedDictionary<ObjectiveId, ObjectiveFeasibilityChange>();
        for (var r = 0; r < _rulePaths.Length; r++)
        {
            var rule = _mission.Rules[r];
            if (_state.TryGet($"{StatePaths.Mission}rule.{rule.Id}.active", out var active) && active.AsBool()) continue;
            if (!targets.Any(_rulePaths[r].Contains)) continue;
            foreach (var objective in rule.ObjectivesUnacceptable)
            {
                if (!_state.TryGet($"{StatePaths.Mission}objective.{objective}.state", out var state) || state.AsText() != Achievable) continue;
                changes[objective] = new ObjectiveFeasibilityChange(objective, Achievable, UnacceptableRisk);
            }
        }
        return [.. changes.Values];
    }

    private List<string> Targets(ResolvedCommand command)
    {
        var targets = new List<string>();
        switch (command.Action.Kind)
        {
            case ActionKind.Remote:
                foreach (var effect in command.Action.BuildEffects(command.Args)) targets.Add(effect.Target);
                break;
            case ActionKind.CrewTask when command.Action.Definition.TaskId is { } taskId && _tasks.TryGet(taskId, out var task):
                var arguments = new TaskArguments(command.Args.Values.Select(a => new KeyValuePair<string, StateValue>(a.Key, a.Value.ToStateValue())));
                var bound = _binder.Bind(task, arguments);
                if (bound.IsSuccess)
                {
                    foreach (var effect in bound.Task.OnSuccess) targets.Add(effect.Target);
                }
                break;
        }
        return targets;
    }
}
