using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>A mission's compiled hint tree. Problems are sorted by id so iteration is deterministic.</summary>
public sealed class HintTree
{
    public HintTree(string missionId, double cooldownS, StuckTuning stuck, IEnumerable<HintProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        MissionId = missionId;
        CooldownS = cooldownS;
        Stuck = stuck;
        var sorted = problems.ToArray();
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        Problems = sorted;
    }

    public string MissionId { get; }

    public double CooldownS { get; }

    public StuckTuning Stuck { get; }

    public IReadOnlyList<HintProblem> Problems { get; }
}

/// <summary>An authored problem with its five stage texts.</summary>
public sealed class HintProblem
{
    private readonly HashSet<string> _referencedFacts = new(StringComparer.Ordinal);

    /// <param name="stages">Exactly five, ordered nudge to direct.</param>
    public HintProblem(string id, int priority, IExpression activation, IExpression resolution, IReadOnlyList<HintStageText> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count != 5) throw new ArgumentException("A problem needs exactly five stages.", nameof(stages));
        Id = id;
        Priority = priority;
        Activation = activation;
        Resolution = resolution;
        Stages = stages;

        foreach (var stage in stages)
        {
            foreach (var fact in stage.Default.FactRefs) _referencedFacts.Add(fact.Value);
            foreach (var alternative in stage.Alternatives)
            {
                foreach (var fact in alternative.Content.FactRefs) _referencedFacts.Add(fact.Value);
            }
        }
    }

    public string Id { get; }

    public int Priority { get; }

    public IExpression Activation { get; }

    public IExpression Resolution { get; }

    public IReadOnlyList<HintStageText> Stages { get; }

    public HintStageText Stage(HintStage stage) => Stages[(int)stage - 1];

    /// <summary>True when any stage points the player at <paramref name="fact"/>, i.e. learning it is progress here.</summary>
    public bool References(FactId fact) => _referencedFacts.Contains(fact.Value);
}

/// <summary>The words and references a tip carries.</summary>
public sealed record HintContent(string Text, IReadOnlyList<FactId> FactRefs, IReadOnlyList<ActionId> ActionRefs);

public sealed record HintAlternative(IExpression When, HintContent Content);

public sealed record HintStageText(HintStage Stage, HintContent Default, IReadOnlyList<HintAlternative> Alternatives)
{
    /// <summary>The first alternative whose condition holds, otherwise the default wording.</summary>
    public HintContent Resolve(IStateQuery state)
    {
        foreach (var alternative in Alternatives)
        {
            if (GuidanceConditions.Holds(alternative.When, state)) return alternative.Content;
        }
        return Default;
    }
}
