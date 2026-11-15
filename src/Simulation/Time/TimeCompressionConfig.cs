using Usa.Simulation.Data;

namespace Usa.Simulation.Time;

/// <summary>Tuning for <see cref="TimeCompressionDirector"/> (<c>config/time_compression.json</c>, GDD 3.2).</summary>
/// <remarks>
/// The optional parameters default to the original rule set (every hold at 1x, every scheduled event approached down to 1x,
/// no event windows), so a file that leaves them out behaves as before.
/// </remarks>
/// <param name="Rates">Compression ladder, strictly ascending, starting at 1.</param>
/// <param name="MaxStableAngularRateDps">Above this angular rate the rate is capped at <paramref name="SpinMaxRate"/>.</param>
/// <param name="TaskRunningMaxRate">Highest rate while a crew task runs.</param>
/// <param name="WorseningMaxRate">Highest rate while any condition is worsening (1 holds real time for an unacknowledged warning).</param>
/// <param name="StableRealSecondsPerStep">Real seconds of calm at one rung before stepping up to the next.</param>
/// <param name="EventLeadMetS">
/// MET before an event at which its approach ends: from here a decisive event holds 1x and any other scheduled event holds
/// <paramref name="ScheduledEventFloorRate"/>.
/// </param>
/// <param name="EventApproachRealS">
/// Approaching an event, a rate is allowed only if reaching the event's lead window at that rate takes at least this many real
/// seconds (the cap is the remaining MET gap divided by this), so the rate falls off smoothly instead of in rungs.
/// </param>
/// <param name="SmoothingRealS">Real seconds over which a rate change is blended (log scale).</param>
/// <param name="DangerousAngularRateDps">At or above this angular rate the director holds 1x (I5 threshold).</param>
/// <param name="SpinMaxRate">Highest rate while the angular rate is above stable but below dangerous.</param>
/// <param name="EntryMaxRate">Highest rate below the entry interface, between event windows.</param>
/// <param name="ParachuteMaxRate">Highest rate under parachutes, between event windows.</param>
/// <param name="PredictedDescentMaxRate">
/// Entry and parachute cap while the host predicts the next decisive event (the approach then slows down in time, so the descent
/// can run faster between windows); null keeps <paramref name="EntryMaxRate"/> and <paramref name="ParachuteMaxRate"/>.
/// </param>
/// <param name="OpeningMaxRate">Highest rate during the calm opening (<see cref="CompressionInputs.CalmOpening"/>).</param>
/// <param name="ScheduledEventFloorRate">The rate a non-decisive scheduled event (task end, readout) is approached down to.</param>
/// <param name="EventLagMetS">MET after a decisive event during which the director holds 1x.</param>
/// <param name="AdvanceLeadMetS">MET before the next event at which "Advance to next event" stops; null uses <paramref name="EventLeadMetS"/>.</param>
/// <param name="PeakGMinG">Below the entry interface, a load at or above this that has levelled off (the peak) holds 1x.</param>
/// <param name="PeakGMaxSlopeGPerS">The load counts as levelled off while it changes by at most this many g per MET second.</param>
/// <param name="TipEscalationMaxRate">Highest rate while a guidance tip is about to escalate (<see cref="CompressionInputs.TipEscalationPending"/>).</param>
/// <param name="RecoveryWaitRealSeconds">
/// Real seconds the whole recovery wait takes, however long it is in MET (<see cref="CompressionInputs.SecondsToRecoveryMet"/>):
/// the rate is the remaining wait over the remaining real budget, above the ladder and every other rule. Null keeps the normal
/// rules for the wait.
/// </param>
/// <param name="TaskRealSecondsMax">
/// Real seconds a crew task may take at most while nothing needs the player (task fast-forward,
/// <see cref="CompressionInputs.CrewTaskSecondsLeftMet"/>): the rate rises above the ladder and the normal caps so the rest of the
/// task fits the remaining budget, easing out towards its end. Alarms, warnings, a pending confirmation, a tutorial step waiting
/// for the player, burns, a dangerous spin and decisive-event windows still win. Null keeps the normal rules for tasks.
/// </param>
/// <param name="TaskRealSecondsByKind">Per task kind (<c>eva</c>, <c>repair</c>...) overrides of <paramref name="TaskRealSecondsMax"/>.</param>
/// <param name="WaitRealSecondsMax">
/// The same budget for the other waits the player only sits through: the coast to a held burn order's mark
/// (<see cref="CompressionInputs.SecondsToHeldOrderMet"/>) and the descent under parachutes up to the next decisive event's lead.
/// Null keeps the normal rules for them.
/// </param>
/// <param name="DescentRealSecondsMax">
/// Real-time budget of the descent under parachutes, so the fall plays visibly fast-forwarded instead of in a blink; null uses
/// <paramref name="WaitRealSecondsMax"/>. With a splashdown prediction (<see cref="CompressionInputs.SecondsToSplashdownMet"/>)
/// it is the whole fall from the drogue to splashdown as one time-lapse (<see cref="DescentTimeLapse"/>); without one, each
/// stretch to the next decisive event's lead.
/// </param>
/// <param name="BurnMaxRate">
/// Highest rate while a burn runs: 1 (the default) makes a burn a 1x hold; above 1 a burn only caps the rate (the burn-stop window
/// still shows its end at 1x).
/// </param>
/// <param name="AdvanceRateMax">
/// Rate of "Advance to next event" (key N): it runs at this rate (or the automatic one, if higher) until its target, a safety stop
/// or a guide step waiting for the player. Null runs it as fast as the frame budget allows.
/// </param>
/// <param name="AdvanceOpenMetS">
/// With no event ahead (a quiet coast), "Advance to next event" may still run up to this much MET, stopping earlier at anything
/// that needs the player. Null offers no advance without an event ahead.
/// </param>
/// <param name="GuidedProfile">Overrides in force during the guided first run (<see cref="CompressionInputs.GuidedFirstRun"/>).</param>
/// <param name="EntryRealSecondsMax">
/// Real-time budget for each stretch of the way down before the chutes: the coast to the entry interface and the fall between
/// the entry's decisive moments (interface, peak g, drogue), each to the next decisive event's lead, fast-forwarded like the
/// other waits. Only applies while no other scheduled event (a burn start, a task end) comes first. Null keeps the normal rules.
/// </param>
/// <param name="PeakGHoldMetS">
/// Most MET the peak-g hold (<paramref name="PeakGMinG"/>) lasts per entry; null holds 1x for as long as the load stays level.
/// </param>
/// <param name="DescentSlowRate">The descent time-lapse's slowest rate: at each chute's deploy moment and at touchdown.</param>
/// <param name="DescentEaseRealS">
/// Time constant (real seconds) of the time-lapse's ease into and out of a chute's deploy moment, which replaces the 1x event
/// windows around the drogue and the main while a splashdown is predicted.
/// </param>
/// <param name="DescentTaperRealS">Time constant (real seconds) of the time-lapse's ease-out into touchdown.</param>
public sealed record TimeCompressionConfig(
    string Schema,
    IReadOnlyList<double> Rates,
    double MaxStableAngularRateDps,
    double TaskRunningMaxRate,
    double WorseningMaxRate,
    double StableRealSecondsPerStep,
    double EventLeadMetS,
    double EventApproachRealS,
    double SmoothingRealS,
    double DangerousAngularRateDps = double.PositiveInfinity,
    double SpinMaxRate = 1,
    double EntryMaxRate = 1,
    double ParachuteMaxRate = 1,
    double? PredictedDescentMaxRate = null,
    double OpeningMaxRate = double.PositiveInfinity,
    double ScheduledEventFloorRate = 1,
    double EventLagMetS = 0,
    double? AdvanceLeadMetS = null,
    double PeakGMinG = double.PositiveInfinity,
    double PeakGMaxSlopeGPerS = 0.05,
    double TipEscalationMaxRate = 10,
    double? RecoveryWaitRealSeconds = null,
    double? TaskRealSecondsMax = null,
    IReadOnlyDictionary<string, double>? TaskRealSecondsByKind = null,
    double? WaitRealSecondsMax = null,
    double BurnMaxRate = 1,
    double? AdvanceRateMax = null,
    double? AdvanceOpenMetS = null,
    TimeCompressionProfile? GuidedProfile = null,
    double? DescentRealSecondsMax = null,
    double? EntryRealSecondsMax = null,
    double? PeakGHoldMetS = null,
    double DescentSlowRate = 1.5,
    double DescentEaseRealS = 0.2,
    double DescentTaperRealS = 0.4) : IVersionedDefinition
{
    public const string SchemaTag = "time_compression/1";
    public const string DefaultPath = "config/time_compression.json";

    /// <summary>This config with <see cref="GuidedProfile"/> applied; this config itself when it has none.</summary>
    public TimeCompressionConfig Guided => GuidedProfile is { } profile ? profile.ApplyTo(this) : this;

    /// <summary>The task fast-forward budget for a task of <paramref name="kind"/>; null when tasks are not fast-forwarded.</summary>
    public double? TaskRealSecondsFor(string? kind) =>
        kind is not null && TaskRealSecondsByKind is { } byKind && byKind.TryGetValue(kind, out var seconds) ? seconds : TaskRealSecondsMax;

    /// <summary>Where "Advance to next event" stops, in MET before the event.</summary>
    public double EffectiveAdvanceLeadMetS => AdvanceLeadMetS ?? EventLeadMetS;

    public static TimeCompressionConfig Load(DefinitionLoader loader, string relativePath = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var config = loader.Load<TimeCompressionConfig>(relativePath, SchemaTag);
        config.Validate(relativePath);
        return config;
    }

    /// <exception cref="DefinitionException">Every problem found, with JSON paths.</exception>
    public void Validate(string file)
    {
        var errors = new DefinitionErrors();
        if (Rates.Count == 0) errors.Add(file, "$.rates", "At least one rate is required.");
        for (var i = 0; i < Rates.Count; i++)
        {
            if (!double.IsFinite(Rates[i]) || Rates[i] < 1)
                errors.Add(file, $"$.rates[{i}]", "Rates must be finite and at least 1.");
            else if (i > 0 && Rates[i] <= Rates[i - 1])
                errors.Add(file, $"$.rates[{i}]", "Rates must be strictly ascending.");
        }
        if (Rates.Count > 0 && Rates[0] != 1) errors.Add(file, "$.rates[0]", "The first rate must be 1 (real time).");

        RequireAtLeast(errors, file, "$.maxStableAngularRateDps", MaxStableAngularRateDps, 0, inclusive: false);
        RequireAtLeast(errors, file, "$.taskRunningMaxRate", TaskRunningMaxRate, 1, inclusive: true);
        RequireAtLeast(errors, file, "$.worseningMaxRate", WorseningMaxRate, 1, inclusive: true);
        RequireAtLeast(errors, file, "$.stableRealSecondsPerStep", StableRealSecondsPerStep, 0, inclusive: true);
        RequireAtLeast(errors, file, "$.eventLeadMetS", EventLeadMetS, 0, inclusive: true);
        RequireAtLeast(errors, file, "$.eventApproachRealS", EventApproachRealS, 0, inclusive: true);
        RequireAtLeast(errors, file, "$.smoothingRealS", SmoothingRealS, 0, inclusive: true);
        if (!(DangerousAngularRateDps > MaxStableAngularRateDps))
            errors.Add(file, "$.dangerousAngularRateDps", "Must be greater than maxStableAngularRateDps.");
        RequireAtLeast(errors, file, "$.spinMaxRate", SpinMaxRate, 1, inclusive: true);
        RequireAtLeast(errors, file, "$.entryMaxRate", EntryMaxRate, 1, inclusive: true);
        RequireAtLeast(errors, file, "$.parachuteMaxRate", ParachuteMaxRate, 1, inclusive: true);
        if (PredictedDescentMaxRate is { } predicted) RequireAtLeast(errors, file, "$.predictedDescentMaxRate", predicted, 1, inclusive: true);
        RequireAtLeast(errors, file, "$.openingMaxRate", OpeningMaxRate, 1, inclusive: true, allowInfinity: true);
        RequireAtLeast(errors, file, "$.scheduledEventFloorRate", ScheduledEventFloorRate, 1, inclusive: true);
        RequireAtLeast(errors, file, "$.eventLagMetS", EventLagMetS, 0, inclusive: true);
        if (AdvanceLeadMetS is { } advanceLead) RequireAtLeast(errors, file, "$.advanceLeadMetS", advanceLead, EventLeadMetS, inclusive: true);
        RequireAtLeast(errors, file, "$.peakGMinG", PeakGMinG, 0, inclusive: false, allowInfinity: true);
        RequireAtLeast(errors, file, "$.peakGMaxSlopeGPerS", PeakGMaxSlopeGPerS, 0, inclusive: true);
        RequireAtLeast(errors, file, "$.tipEscalationMaxRate", TipEscalationMaxRate, 1, inclusive: true);
        if (RecoveryWaitRealSeconds is { } recovery) RequireAtLeast(errors, file, "$.recoveryWaitRealSeconds", recovery, 0, inclusive: false);
        if (TaskRealSecondsMax is { } task) RequireAtLeast(errors, file, "$.taskRealSecondsMax", task, 0, inclusive: false);
        if (TaskRealSecondsByKind is { } byKind)
        {
            foreach (var (kind, seconds) in byKind) RequireAtLeast(errors, file, $"$.taskRealSecondsByKind.{kind}", seconds, 0, inclusive: false);
        }
        if (WaitRealSecondsMax is { } wait) RequireAtLeast(errors, file, "$.waitRealSecondsMax", wait, 0, inclusive: false);
        if (DescentRealSecondsMax is { } descent) RequireAtLeast(errors, file, "$.descentRealSecondsMax", descent, 0, inclusive: false);
        if (EntryRealSecondsMax is { } entry) RequireAtLeast(errors, file, "$.entryRealSecondsMax", entry, 0, inclusive: false);
        if (PeakGHoldMetS is { } peakHold) RequireAtLeast(errors, file, "$.peakGHoldMetS", peakHold, 0, inclusive: true);
        RequireAtLeast(errors, file, "$.burnMaxRate", BurnMaxRate, 1, inclusive: true);
        RequireAtLeast(errors, file, "$.descentSlowRate", DescentSlowRate, 1, inclusive: true);
        RequireAtLeast(errors, file, "$.descentEaseRealS", DescentEaseRealS, 0, inclusive: false);
        RequireAtLeast(errors, file, "$.descentTaperRealS", DescentTaperRealS, 0, inclusive: false);
        if (AdvanceRateMax is { } advanceRate) RequireAtLeast(errors, file, "$.advanceRateMax", advanceRate, 1, inclusive: true);
        if (AdvanceOpenMetS is { } open) RequireAtLeast(errors, file, "$.advanceOpenMetS", open, 0, inclusive: false);
        errors.ThrowIfAny();
        GuidedProfile?.ApplyTo(this).Validate(file + " (guidedProfile)");
    }

    private static void RequireAtLeast(DefinitionErrors errors, string file, string path, double value, double min, bool inclusive, bool allowInfinity = false)
    {
        var inRange = inclusive ? value >= min : value > min;
        var ok = (double.IsFinite(value) || (allowInfinity && double.IsPositiveInfinity(value))) && inRange;
        if (!ok) errors.Add(file, path, inclusive ? $"Must be a finite number >= {min}." : $"Must be a finite number > {min}.");
    }
}
