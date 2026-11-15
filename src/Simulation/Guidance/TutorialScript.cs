using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>A compiled guided-first-run script.</summary>
public sealed record TutorialScript(string MissionId, double MinDisplayS, IExpression? EndWhen, IReadOnlyList<TutorialBeat> Beats);

/// <param name="AfterIndex">Index of the prerequisite beat, or -1.</param>
public sealed record TutorialBeat(
    string Id,
    string Anchor,
    string Text,
    IExpression Trigger,
    IExpression? Completion,
    int AfterIndex,
    double? MaxDisplayS,
    IReadOnlyList<TutorialTextAlternative> Alternatives,
    string? Title = null,
    TutorialOrder? SuggestedOrder = null,
    double? MaxTimeRate = null,
    TutorialRailData? Rail = null)
{
    public string ResolveText(IStateQuery state) => Matching(state)?.Text ?? Text;

    /// <summary>The order the callout offers now: the matching alternative's, else the beat's own.</summary>
    public TutorialOrder? ResolveOrder(IStateQuery state) => Matching(state)?.SuggestedOrder ?? SuggestedOrder;

    private TutorialTextAlternative? Matching(IStateQuery state)
    {
        foreach (var alternative in Alternatives)
        {
            if (GuidanceConditions.Holds(alternative.When, state)) return alternative;
        }
        return null;
    }
}

public sealed record TutorialTextAlternative(IExpression When, string Text, TutorialOrder? SuggestedOrder = null);

/// <summary>An order a callout offers ("Send: ‘June, read the thruster temps’"); the UI submits it as a structured request.</summary>
/// <param name="Crew">Assignee; null lets the session choose.</param>
/// <param name="Text">The order as the player would say it.</param>
public sealed record TutorialOrder(ActionId Action, CrewId? Crew, IReadOnlyDictionary<string, CommandArg> Args, string Text);
