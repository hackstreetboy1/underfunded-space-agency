using Usa.Simulation.Kernel;

namespace Usa.Simulation.Guidance;

/// <summary>
/// Stuck score of one open problem, in stuck-seconds (GDD 9.1). It rises with time without progress,
/// faster with unresolved critical alarms or no new diagnostic information, jumps on failed actions,
/// holds while the player is productively busy, and falls on diagnostics, repairs and stabilizations.
/// </summary>
public sealed class StuckTracker(StuckTuning tuning) : IHashContributor
{
    public double ScoreS { get; private set; }

    public double SinceNewInformationS { get; private set; }

    public void Advance(double deltaS, in GuidanceActivity activity)
    {
        SinceNewInformationS += deltaS;
        if (activity.SuspendsStuck) return;

        var rate = 1.0;
        if (SinceNewInformationS > tuning.NoNewInfoAfterS) rate += tuning.NoNewInfoExtraRate;
        if (activity.UnresolvedCriticalAlarms > 0) rate += tuning.CriticalAlarmExtraRate;
        ScoreS += rate * deltaS;
    }

    public void RecordFailedAction() => ScoreS += tuning.FailedActionPenaltyS;

    /// <param name="relevant">Whether the information bears on this problem; only then does the score fall.</param>
    public void RecordDiagnostic(bool relevant)
    {
        SinceNewInformationS = 0;
        if (relevant) Relieve(tuning.DiagnosticReliefS);
    }

    public void RecordRepair() => Relieve(tuning.RepairReliefS);

    public void RecordStabilization() => Relieve(tuning.StabilizationReliefS);

    /// <summary>Raises the score to at least <paramref name="scoreS"/>, so escalation continues from a requested stage.</summary>
    public void RaiseTo(double scoreS) => ScoreS = Math.Max(ScoreS, scoreS);

    /// <summary>The strongest stage this score permits: one stage per <paramref name="escalationS"/> stuck-seconds.</summary>
    public HintStage PermittedStage(double escalationS)
    {
        var stage = (int)Math.Floor(ScoreS / escalationS);
        return (HintStage)Math.Clamp(stage, 0, (int)HintStage.Direct);
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ScoreS);
        writer.Write(SinceNewInformationS);
    }

    private void Relieve(double amountS) => ScoreS = Math.Max(0.0, ScoreS - amountS);
}
