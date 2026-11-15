using Usa.Simulation.Kernel;

namespace Usa.Simulation.Time;

/// <summary>What the host reads from simulation state each frame for <see cref="TimeCompressionDirector"/>.</summary>
/// <remarks>
/// Every member defaults to "nothing happening", so a host that does not supply a newer input simply loses that rule. The
/// director also detects some decisive events itself from edges of these inputs (burn stop, docking contact or separation,
/// entry interface, drogue deployment, splashdown).
/// </remarks>
public readonly record struct CompressionInputs
{
    public MissionTime Now { get; init; }

    public bool CriticalAlarmUnacknowledged { get; init; }

    /// <summary>A condition getting worse fast enough that the player must watch it (holds 1x).</summary>
    public bool RapidlyWorsening { get; init; }

    /// <summary>Largest absolute body rate of any crewed vehicle.</summary>
    public double MaxAngularRateDps { get; init; }

    public bool BurnActive { get; init; }

    public bool DockingActive { get; init; }

    public bool BelowEntryInterface { get; init; }

    public bool ParachutePhase { get; init; }

    /// <summary>Current load factor in g; used below the entry interface for the peak-g window.</summary>
    public double GLoad { get; init; }

    public bool CrewTaskRunning { get; init; }

    /// <summary>
    /// A condition is getting worse (the session reports an unacknowledged warning alarm): caps the rate at
    /// <see cref="TimeCompressionConfig.WorseningMaxRate"/> and blocks "Advance to next event".
    /// </summary>
    public bool AnyWorseningCondition { get; init; }

    /// <summary>The mission is in its calm opening (GDD 12.4): caps the rate so the opening lasts a few real minutes.</summary>
    public bool CalmOpening { get; init; }

    /// <summary>MET seconds until the next scheduled event (burn, retrofire, window, task end); null when none.</summary>
    public double? SecondsToNextEventMet { get; init; }

    /// <summary>
    /// MET seconds until the next decisive event (burn start, retrofire, docking contact, entry interface, peak g, drogue, main,
    /// splashdown); the director holds 1x from <see cref="TimeCompressionConfig.EventLeadMetS"/> before it. Null when none is predicted.
    /// </summary>
    public double? SecondsToDecisiveEventMet { get; init; }

    /// <summary>MET seconds since the last decisive event; the director holds 1x for <see cref="TimeCompressionConfig.EventLagMetS"/> after it.</summary>
    public double? SecondsSinceDecisiveEventMet { get; init; }

    /// <summary>
    /// A guidance tip is about to escalate to its next stage: caps the rate at <see cref="TimeCompressionConfig.TipEscalationMaxRate"/>
    /// so the player sees every stage instead of compression skipping past them (guidance itself runs on MET).
    /// </summary>
    public bool TipEscalationPending { get; init; }

    /// <summary>
    /// A guided-first-run callout is waiting for the player to act: the rate stays at or below this (its <c>maxTimeRate</c> in
    /// <c>tutorial.json</c>; 1 holds real time) so the step is not overtaken by events. Null when no callout asks for a cap.
    /// </summary>
    public double? TutorialActionMaxRate { get; init; }

    /// <summary>The difficulty's <c>critical_alert_slowdown</c>: a critical alarm drops to 1x instantly instead of smoothly.</summary>
    public bool CriticalAlertSlowdown { get; init; }

    /// <summary>
    /// MET seconds left in the recovery wait after splashdown (the crew afloat until recovered); null when no recovery is due.
    /// With <see cref="TimeCompressionConfig.RecoveryWaitRealSeconds"/> the director runs the whole wait in that much real time.
    /// </summary>
    public double? SecondsToRecoveryMet { get; init; }

    /// <summary>MET seconds left in the running crew task; null when none runs (task fast-forward, <see cref="TimeCompressionConfig.TaskRealSecondsMax"/>).</summary>
    public double? CrewTaskSecondsLeftMet { get; init; }

    /// <summary>The running crew task's kind as in the task catalog (<c>eva</c>, <c>procedure</c>...), for the per-kind budgets.</summary>
    public string? CrewTaskKind { get; init; }

    /// <summary>
    /// MET seconds to splashdown predicted under the chutes (the rest of the fall at the chutes' terminal speeds, the main's
    /// included while it is stowed); null when not under chutes. With it the director runs the whole descent as one time-lapse
    /// (<see cref="TimeCompressionConfig.DescentRealSecondsMax"/>).
    /// </summary>
    public double? SecondsToSplashdownMet { get; init; }

    /// <summary>
    /// MET seconds to the next stowed chute's nominal deploy altitude while descending in the atmosphere; null when none is
    /// ahead. The approach to a chute's deploy moment eases down to <see cref="TimeCompressionConfig.DescentSlowRate"/> instead
    /// of holding 1x for the event lead.
    /// </summary>
    public double? SecondsToChuteDeployMet { get; init; }

    /// <summary>The host is asking the player to confirm an order: no fast-forward runs past the question.</summary>
    public bool PlayerConfirmationPending { get; init; }

    /// <summary>
    /// MET seconds until a confirmed burn order the host holds is given (its mark); null when none is held. The host stops exactly
    /// at the mark, and the coast to it is a wait (<see cref="TimeCompressionConfig.WaitRealSecondsMax"/>).
    /// </summary>
    public double? SecondsToHeldOrderMet { get; init; }

    /// <summary>
    /// The guided first run is under way (the guide is on, has started and is not finished): the director paces with
    /// <see cref="TimeCompressionConfig.GuidedProfile"/>.
    /// </summary>
    public bool GuidedFirstRun { get; init; }
}
