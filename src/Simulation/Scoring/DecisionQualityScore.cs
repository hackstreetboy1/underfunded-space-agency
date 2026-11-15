namespace Usa.Simulation.Scoring;

/// <summary>
/// Decision quality (GDD 10.1): resource discipline, unnecessary risk, avoidable losses, procedures.
/// Its only inputs are the record's decision metrics and procedures; hint usage does not exist here (D13).
/// </summary>
internal static class DecisionQualityScore
{
    public static DimensionScore Evaluate(DecisionTuning tuning, double weightPoints, MissionRecord record)
    {
        var dimension = new DimensionBuilder(ScoreDimension.DecisionQuality, weightPoints);
        var metrics = record.Decisions;

        var lossTotal = 0.0;
        foreach (var loss in metrics.AvoidableLosses)
        {
            lossTotal += Ramp.Clamp01(loss.Weight);
            dimension.Note("decisions.avoidable_loss", loss.Key, loss.Weight);
        }

        var ret = record.Return;
        var procedures = (ret.EquipmentJettisonedBeforeRetrofire ? 0.5 : 0.0) + (ret.ChutesDeployedInLimits ? 0.5 : 0.0);

        dimension.Line("decisions.propellant_discipline", tuning.PropellantDiscipline,
            Ramp.Down(metrics.AvoidablePropellantWasteKg, 0.0, tuning.PropellantZeroCreditKg));
        dimension.Line("decisions.risk_avoidance", tuning.RiskAvoidance,
            Ramp.Down(metrics.AvoidableRiskExposureS, 0.0, tuning.RiskZeroCreditS));
        dimension.Line("decisions.avoidable_losses", tuning.AvoidableLosses, 1.0 - lossTotal);
        dimension.Line("decisions.procedures", tuning.Procedures, ret.Landed ? procedures : 0.0);
        return dimension.Build();
    }

    /// <summary>Points one avoidable-loss entry costs, used to rank the debrief's largest loss.</summary>
    public static double LossCostPoints(DecisionTuning tuning, double weightPoints, WeightedDecision loss) =>
        weightPoints * tuning.AvoidableLosses * Ramp.Clamp01(loss.Weight);
}
