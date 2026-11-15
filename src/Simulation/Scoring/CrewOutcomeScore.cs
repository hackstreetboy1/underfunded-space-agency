namespace Usa.Simulation.Scoring;

/// <summary>Crew outcome (GDD 10.1): survivors, health retained, worst health, complications, avoidable exposure.</summary>
internal static class CrewOutcomeScore
{
    public static DimensionScore Evaluate(CrewOutcomeTuning tuning, double weightPoints, MissionRecord record)
    {
        var dimension = new DimensionBuilder(ScoreDimension.CrewOutcome, weightPoints);
        var count = record.CrewCount;

        var retained = 0.0;
        var worst = 0.0;
        var free = 0;
        foreach (var member in record.Crew)
        {
            if (!member.Survived)
            {
                dimension.Note("crew.lost", member.Crew.Value);
                continue;
            }

            retained += Ramp.Ratio(member.HealthAtEnd, member.HealthAtLaunch);
            worst += Ramp.Ratio(member.WorstHealth, member.HealthAtLaunch);

            var loss = record.MissionCausedLossPoints(member.Crew);
            if (loss > tuning.ComplicationThresholdPoints) dimension.Note("crew.complication", member.Crew.Value, loss);
            else free++;
        }

        foreach (var loss in record.HealthLosses)
        {
            dimension.Note("crew.health_loss." + loss.Cause, loss.Crew.Value, loss.LossPoints);
        }

        dimension.Line("crew.survival", tuning.Survival, (double)record.SurvivorCount / count);
        dimension.Line("crew.health_retained", tuning.HealthRetained, retained / count);
        dimension.Line("crew.worst_health", tuning.WorstHealth, worst / count);
        dimension.Line("crew.complications", tuning.Complications, (double)free / count);
        dimension.Line("crew.avoidable_exposure", tuning.AvoidableExposure,
            Ramp.Down(record.Decisions.AvoidableRiskExposureS, 0.0, tuning.ExposureZeroCreditS));
        return dimension.Build();
    }
}
