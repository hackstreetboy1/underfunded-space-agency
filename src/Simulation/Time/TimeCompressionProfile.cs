namespace Usa.Simulation.Time;

/// <summary>
/// Overrides of <see cref="TimeCompressionConfig"/> applied while a pacing condition holds (<c>guidedProfile</c> in
/// <c>config/time_compression.json</c>: the guided first run, <see cref="CompressionInputs.GuidedFirstRun"/>). Each member left
/// null keeps the base value, the rate ladder included.
/// </summary>
/// <param name="Rates">The guided ladder (same rules as the base one); null shares the base ladder.</param>
/// <param name="BurnMaxRate">
/// Highest rate while a burn runs. Null (or 1) keeps a burn a 1x hold; above 1 a burn only caps the rate, and the burn-stop window
/// (<see cref="TimeCompressionConfig.EventLagMetS"/>) still shows its end at 1x.
/// </param>
public sealed record TimeCompressionProfile(
    double? OpeningMaxRate = null,
    double? StableRealSecondsPerStep = null,
    double? EventLeadMetS = null,
    double? EventLagMetS = null,
    double? EventApproachRealS = null,
    double? SmoothingRealS = null,
    double? EntryMaxRate = null,
    double? ParachuteMaxRate = null,
    double? PredictedDescentMaxRate = null,
    double? PeakGMinG = null,
    double? TaskRunningMaxRate = null,
    double? ScheduledEventFloorRate = null,
    double? BurnMaxRate = null,
    double? AdvanceLeadMetS = null,
    double? RecoveryWaitRealSeconds = null,
    double? TaskRealSecondsMax = null,
    IReadOnlyDictionary<string, double>? TaskRealSecondsByKind = null,
    double? WaitRealSecondsMax = null,
    double? DescentRealSecondsMax = null,
    IReadOnlyList<double>? Rates = null,
    double? EntryRealSecondsMax = null,
    double? PeakGHoldMetS = null)
{
    /// <summary><paramref name="baseConfig"/> with this profile's overrides, without a profile of its own.</summary>
    public TimeCompressionConfig ApplyTo(TimeCompressionConfig baseConfig)
    {
        ArgumentNullException.ThrowIfNull(baseConfig);
        return baseConfig with
        {
            OpeningMaxRate = OpeningMaxRate ?? baseConfig.OpeningMaxRate,
            StableRealSecondsPerStep = StableRealSecondsPerStep ?? baseConfig.StableRealSecondsPerStep,
            EventLeadMetS = EventLeadMetS ?? baseConfig.EventLeadMetS,
            EventLagMetS = EventLagMetS ?? baseConfig.EventLagMetS,
            EventApproachRealS = EventApproachRealS ?? baseConfig.EventApproachRealS,
            SmoothingRealS = SmoothingRealS ?? baseConfig.SmoothingRealS,
            EntryMaxRate = EntryMaxRate ?? baseConfig.EntryMaxRate,
            ParachuteMaxRate = ParachuteMaxRate ?? baseConfig.ParachuteMaxRate,
            PredictedDescentMaxRate = PredictedDescentMaxRate ?? baseConfig.PredictedDescentMaxRate,
            PeakGMinG = PeakGMinG ?? baseConfig.PeakGMinG,
            TaskRunningMaxRate = TaskRunningMaxRate ?? baseConfig.TaskRunningMaxRate,
            ScheduledEventFloorRate = ScheduledEventFloorRate ?? baseConfig.ScheduledEventFloorRate,
            BurnMaxRate = BurnMaxRate ?? baseConfig.BurnMaxRate,
            AdvanceLeadMetS = AdvanceLeadMetS ?? baseConfig.AdvanceLeadMetS,
            RecoveryWaitRealSeconds = RecoveryWaitRealSeconds ?? baseConfig.RecoveryWaitRealSeconds,
            TaskRealSecondsMax = TaskRealSecondsMax ?? baseConfig.TaskRealSecondsMax,
            TaskRealSecondsByKind = TaskRealSecondsByKind ?? baseConfig.TaskRealSecondsByKind,
            WaitRealSecondsMax = WaitRealSecondsMax ?? baseConfig.WaitRealSecondsMax,
            DescentRealSecondsMax = DescentRealSecondsMax ?? baseConfig.DescentRealSecondsMax,
            Rates = Rates ?? baseConfig.Rates,
            EntryRealSecondsMax = EntryRealSecondsMax ?? baseConfig.EntryRealSecondsMax,
            PeakGHoldMetS = PeakGHoldMetS ?? baseConfig.PeakGHoldMetS,
            GuidedProfile = null,
        };
    }
}
