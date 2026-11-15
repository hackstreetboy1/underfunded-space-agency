namespace Usa.Simulation.Scoring;

/// <summary>Safe return (GDD 10.1): landed, entry loads, recovery zone and wait, procedures.</summary>
internal static class SafeReturnScore
{
    public static DimensionScore Evaluate(SafeReturnTuning tuning, double weightPoints, MissionRecord record)
    {
        var dimension = new DimensionBuilder(ScoreDimension.SafeReturn, weightPoints);
        var ret = record.Return;

        if (!ret.Landed)
        {
            dimension.Note("return.not_landed");
            dimension.Line("return.landed", tuning.Landed, 0);
            dimension.Line("return.entry_loads", tuning.EntryLoads, 0);
            dimension.Line("return.recovery_zone", tuning.RecoveryZone, 0);
            dimension.Line("return.recovery_wait", tuning.RecoveryWait, 0);
            dimension.Line("return.procedures", tuning.Procedures, 0);
            return dimension.Build();
        }

        var zoneCredit = ret.RecoveryZoneId is { } zone && tuning.ZoneCredit.TryGetValue(zone, out var credit)
            ? credit
            : tuning.UnlistedZoneCredit;
        if (ret.RecoveryZoneId is null) dimension.Note("return.outside_recovery_zones");

        var procedures = (ret.EquipmentJettisonedBeforeRetrofire ? 0.5 : 0.0) + (ret.ChutesDeployedInLimits ? 0.5 : 0.0);
        if (!ret.EquipmentJettisonedBeforeRetrofire) dimension.Note("return.equipment_not_jettisoned");
        if (!ret.ChutesDeployedInLimits) dimension.Note("return.chutes_out_of_limits");

        dimension.Line("return.landed", tuning.Landed, 1.0);
        dimension.Line("return.entry_loads", tuning.EntryLoads, Ramp.Down(ret.EntryPeakG, tuning.EntryFullCreditG, tuning.EntryZeroCreditG));
        dimension.Line("return.recovery_zone", tuning.RecoveryZone, zoneCredit, ret.RecoveryZoneId);
        dimension.Line("return.recovery_wait", tuning.RecoveryWait, Ramp.Down(ret.RecoveryWaitHours, tuning.WaitFullCreditH, tuning.WaitZeroCreditH));
        dimension.Line("return.procedures", tuning.Procedures, procedures);
        return dimension.Build();
    }
}
