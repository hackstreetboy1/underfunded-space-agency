using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>Difficulty-dependent guidance settings (GDD 9.3).</summary>
/// <param name="EscalationS">Stuck-seconds per stage (<c>difficulty.tip_escalation_s</c>).</param>
/// <param name="MaxUnsolicitedStage">Strongest stage shown without a request; Hard stops before the direct solution.</param>
public sealed record GuidanceSettings(double EscalationS, HintStage MaxUnsolicitedStage = HintStage.Direct)
{
    public const string EscalationPath = StatePaths.Difficulty + "tip_escalation_s";
    public const string MaxUnsolicitedStagePath = StatePaths.Difficulty + "tip_max_unsolicited_stage";

    /// <summary>Reads <c>difficulty.tip_escalation_s</c> and the optional <c>difficulty.tip_max_unsolicited_stage</c> (1-5).</summary>
    public static GuidanceSettings FromDifficulty(IStateQuery state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var escalationS = state.GetNumber(EscalationPath);
        if (escalationS <= 0) throw new InvalidOperationException($"{EscalationPath} must be positive but is {escalationS}.");

        var maxStage = HintStage.Direct;
        if (state.TryGet(MaxUnsolicitedStagePath, out var value))
            maxStage = (HintStage)Math.Clamp((int)value.AsNumber(), (int)HintStage.None, (int)HintStage.Direct);

        return new GuidanceSettings(escalationS, maxStage);
    }
}
