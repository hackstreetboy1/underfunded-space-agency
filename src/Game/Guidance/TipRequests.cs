using Usa.Game.Presentation.ViewModels;
using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Guidance;

namespace Usa.Game.Guidance;

/// <summary>
/// Builds the <c>/v1/tip</c> request for one authored tip. It carries the stage, the authored text, the tip's own
/// fact refs with their labels and qualitative bands, and its action refs: never readings, other facts or hidden state.
/// </summary>
public static class TipRequests
{
    /// <summary>Proxy limits (<c>app/schemas/tip.py</c>).</summary>
    public const int MaxKnownFacts = 32;

    public const int MaxLabelLength = 120;

    public static TipRequest Build(TipAvailable tip, string requestId, ITipFactSource facts)
    {
        ArgumentNullException.ThrowIfNull(tip);
        ArgumentException.ThrowIfNullOrEmpty(requestId);
        ArgumentNullException.ThrowIfNull(facts);

        var stage = ToContract(tip.Stage);
        var factRefs = new List<string>(tip.FactRefs.Count);
        var known = new List<KnownFact>(tip.FactRefs.Count);
        foreach (var fact in tip.FactRefs)
        {
            if (factRefs.Contains(fact.Value, StringComparer.Ordinal)) continue;
            factRefs.Add(fact.Value);
            if (known.Count < MaxKnownFacts && facts.TryDescribe(fact, tip.Time, out var info))
                known.Add(new KnownFact(fact.Value, Label(info)));
        }

        var actions = new List<string>(tip.ActionRefs.Count);
        foreach (var action in tip.ActionRefs)
        {
            if (!actions.Contains(action.Value, StringComparer.Ordinal)) actions.Add(action.Value);
        }

        var candidate = new HintCandidate(tip.ProblemId, stage, tip.Text) { FactRefs = factRefs, AllowedActionRefs = actions };
        return new TipRequest(requestId, stage, [candidate]) { KnownFacts = known };
    }

    public static TipStage ToContract(HintStage stage) => stage switch
    {
        HintStage.Nudge => TipStage.Nudge,
        HintStage.Investigate => TipStage.Investigate,
        HintStage.Connect => TipStage.Connect,
        HintStage.Recommend => TipStage.Recommend,
        HintStage.Direct => TipStage.Direct,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Only shown stages are rephrased."),
    };

    /// <summary><c>OAMS propellant (falling)</c>; the band only when the ground actually has the fact.</summary>
    private static string Label(TipFactInfo info)
    {
        var label = info.Knowledge != ReadoutKnowledge.Unknown && !string.IsNullOrWhiteSpace(info.Band)
            ? $"{info.Label} ({info.Band.Trim()})"
            : info.Label;
        return label.Length <= MaxLabelLength ? label : label[..MaxLabelLength].TrimEnd();
    }
}
