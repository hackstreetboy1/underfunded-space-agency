using Usa.Simulation.Kernel;

namespace Usa.Simulation.Time;

/// <summary>
/// Chooses the automatic time-compression rate from simulation state (GDD 3.2, SCRIPTING 11.7).
/// Runs in the host on real time; it never touches simulation state, so it does not affect determinism.
/// </summary>
/// <remarks>
/// 1x is reserved for decisive moments. In priority order:
/// <list type="number">
/// <item><b>Holds</b> (1x, blended): an unacknowledged critical alarm, a rapidly worsening condition, an angular rate at
/// or above <see cref="TimeCompressionConfig.DangerousAngularRateDps"/>, a burn, docking or undocking.</item>
/// <item><b>Event windows</b> (1x at once): <see cref="TimeCompressionConfig.EventLeadMetS"/> before a decisive event and
/// <see cref="TimeCompressionConfig.EventLagMetS"/> after one (the host's predictions, plus the edges the director sees itself:
/// burn stop, docking contact or separation, entry interface, drogue, splashdown), and the loads around peak g.</item>
/// <item><b>Caps</b>: moderate spin, entry, parachutes, a worsening condition (an unacknowledged warning alarm; 1x as shipped),
/// a crew task, the calm opening and a guidance tip about to escalate each cap the rate;
/// approaching an event caps it at the remaining gap divided by <see cref="TimeCompressionConfig.EventApproachRealS"/>, down to
/// 1x for a decisive event and to <see cref="TimeCompressionConfig.ScheduledEventFloorRate"/> for any other scheduled one.
/// Under the caps the rate climbs one rung of the ladder per <see cref="TimeCompressionConfig.StableRealSecondsPerStep"/>.</item>
/// <item><b>Fast-forward</b>: a wait the player only sits through (a crew task, docking included; the coast to a held order's
/// mark; the descent under chutes) runs in at most its real budget, above the caps and the docking hold but never past an
/// alarm, warning, burn, dangerous spin, event window, confirmation or tutorial step waiting for the player. With a splashdown
/// prediction the descent is one time-lapse from the drogue to splashdown (<see cref="DescentTimeLapse"/>): the chutes' deploy
/// moments and touchdown are eased into rather than held at 1x.</item>
/// </list>
/// Changes blend over <see cref="TimeCompressionConfig.SmoothingRealS"/>, except that the output never exceeds an approach cap,
/// event windows apply at once and, with <see cref="CompressionInputs.CriticalAlertSlowdown"/>, a critical alarm drops to 1x at once.
/// During the guided first run the rules use <see cref="TimeCompressionConfig.GuidedProfile"/> (shorter budgets and windows).
/// "Advance to next event" is offered while nothing needs the player (no hold but docking, window, spin, entry, warning, burn,
/// confirmation or guide step waiting for them; on the guided run a moderate spin and the way down do not count) and runs to the
/// next event minus the advance lead, or over a quiet coast for at most <see cref="TimeCompressionConfig.AdvanceOpenMetS"/>, at
/// <see cref="AdvanceRateMax"/>.
/// </remarks>
public sealed class TimeCompressionDirector
{
    private const long GSlopeSpanMs = 1000;

    private readonly TimeCompressionConfig _baseConfig;
    private readonly TimeCompressionConfig _guidedConfig;
    private TimeCompressionConfig _config;
    private int _rung;
    private double _calmRealS;
    private double _blendFromRate = 1;
    private double _blendToRate = 1;
    private double _blendRealS;
    private bool _hasPrevious;
    private bool _wasBurning;
    private bool _wasDocking;
    private bool _wasBelowInterface;
    private bool _wasUnderChutes;
    private bool _descentRan;
    private long _windowUntilMs = long.MinValue;
    private CompressionReason _windowReason;
    private long _gSampleMs = long.MinValue;
    private double _gSample;
    private double _gSlopeGPerS = double.NaN;
    private long _peakGSinceMs = long.MinValue;
    private double _recoveryRealLeftS;
    private double _smoothRate = 1;
    private double _fastForwardRate;
    private double _fastForwardRealLeftS;
    private bool _fastForwardIsTask;
    private double _fastForwardLeftS;
    private readonly DescentTimeLapse _descent = new();

    public TimeCompressionDirector(TimeCompressionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate("time compression config");
        _baseConfig = config;
        _guidedConfig = config.Guided;
        _config = config;
    }

    /// <summary>The rate the host should run at now (smoothed).</summary>
    public double TargetRate { get; private set; } = 1;

    /// <summary>The unsmoothed rate the rules ask for.</summary>
    public double RuleRate { get; private set; } = 1;

    public CompressionReason Reason { get; private set; } = CompressionReason.StableCoast;

    /// <summary>The recovery-wait rule is in force (<see cref="TimeCompressionConfig.RecoveryWaitRealSeconds"/>).</summary>
    public bool IsRecoveryWait { get; private set; }

    /// <summary>
    /// A wait is being fast-forwarded (<see cref="TimeCompressionConfig.TaskRealSecondsMax"/>,
    /// <see cref="TimeCompressionConfig.WaitRealSecondsMax"/>): the host may give the simulation a larger frame budget.
    /// </summary>
    public bool IsFastForward { get; private set; }

    /// <summary>
    /// The first tick at or after a held burn order's mark (<see cref="CompressionInputs.SecondsToHeldOrderMet"/>): the host must not
    /// run past it, so the order is given at the same MET however fast time runs. Null when no order is held.
    /// </summary>
    public MissionTime? StopAt { get; private set; }

    /// <summary>True when the vehicle is calm (no hold, window, spin, entry or worsening) and an event lies beyond the advance lead.</summary>
    public bool CanAdvanceToNextEvent { get; private set; }

    /// <summary>Next event time minus the advance lead, when an event lies beyond it.</summary>
    public MissionTime? NextEventAdvanceTarget { get; private set; }

    /// <summary>
    /// Rate of "Advance to next event" (<see cref="TimeCompressionConfig.AdvanceRateMax"/> of the config in force); infinity when
    /// the advance runs as fast as the frame budget allows.
    /// </summary>
    public double AdvanceRateMax => _config.AdvanceRateMax ?? double.PositiveInfinity;

    /// <summary>The guided first run's profile is in force (<see cref="CompressionInputs.GuidedFirstRun"/>).</summary>
    public bool IsGuidedPacing { get; private set; }

    private int TopRung => _config.Rates.Count - 1;

    public void Update(in CompressionInputs inputs, double realDeltaS)
    {
        if (!(realDeltaS >= 0) || double.IsInfinity(realDeltaS))
            throw new ArgumentOutOfRangeException(nameof(realDeltaS), realDeltaS, "Real delta must be a finite, non-negative number of seconds.");

        IsGuidedPacing = inputs.GuidedFirstRun && !ReferenceEquals(_guidedConfig, _baseConfig);
        _config = IsGuidedPacing ? _guidedConfig : _baseConfig;
        _rung = Math.Min(_rung, TopRung);
        DetectEdges(inputs);
        StopAt = HeldOrderStop(inputs);
        if (UpdateRecoveryWait(inputs, realDeltaS))
        {
            IsFastForward = false;
            return;
        }
        var hold = HoldReason(inputs);
        if (UpdateDescentTimeLapse(inputs, realDeltaS, hold)) return;
        var window = hold is null ? WindowReason(inputs) : null;
        var approachCap = ApproachCap(inputs);
        if ((hold ?? window) is { } oneX)
        {
            _rung = 0;
            _calmRealS = 0;
            Reason = oneX;
            RuleRate = 1;
        }
        else
        {
            var (cap, reason) = ConditionCap(inputs);
            if (approachCap < cap)
            {
                cap = approachCap;
                reason = CompressionReason.EventApproaching;
            }
            Climb(CeilingRung(cap), realDeltaS);
            Reason = reason;
            RuleRate = Math.Min(_config.Rates[_rung], cap);
        }

        Blend(realDeltaS, snap: window is not null || (hold == CompressionReason.CriticalAlarm && inputs.CriticalAlertSlowdown));
        var immediateCap = window is null ? approachCap : 1;
        if (_smoothRate > immediateCap)
        {
            _smoothRate = immediateCap;
            _blendFromRate = immediateCap;
            _blendRealS = 0;
        }

        TargetRate = _smoothRate;
        // A docking approach may be skipped like a task; a burn, a question to the player or a guide step waiting for them may not.
        var calm = hold is null or CompressionReason.Docking;
        // On the guided run N works whenever nothing needs the player: only an alarm or warning, a dangerous spin, a burn, a
        // decisive window, a question or a guide step waiting for them stops it, not a moderate spin or the way down.
        var steady = IsGuidedPacing ? !inputs.AnyWorseningCondition : IsSteady(inputs);
        UpdateAdvance(inputs, calm && window is null && steady && !inputs.BurnActive &&
                              !inputs.PlayerConfirmationPending && inputs.TutorialActionMaxRate is null);
        UpdateFastForward(inputs, realDeltaS, hold, window);
    }

    /// <summary>
    /// Fast-forwards a wait the player only sits through, so it takes at most its real budget: a crew task
    /// (<see cref="TimeCompressionConfig.TaskRealSecondsFor"/>), the coast to a held order's mark or the descent under chutes
    /// (<see cref="TimeCompressionConfig.WaitRealSecondsMax"/>), the way down to the drogue
    /// (<see cref="TimeCompressionConfig.EntryRealSecondsMax"/>), each up to the next decisive event's lead at the latest. The rate
    /// is twice the MET left over the real budget left, so MET runs out along an ease-out curve that arrives slowly; it only ever
    /// raises the normal rate. Anything that needs the player or is decisive stops it: an unacknowledged alarm or warning, a
    /// rapidly worsening condition, a dangerous spin, a burn, a decisive-event window, a pending confirmation or a tutorial step
    /// waiting for the player. A wait interrupted that way starts a fresh budget.
    /// </summary>
    private void UpdateFastForward(in CompressionInputs inputs, double realDeltaS, CompressionReason? hold, CompressionReason? window)
    {
        double? leftS = null;
        double budgetS = 0;
        var isTask = false;
        if (inputs.CrewTaskSecondsLeftMet is > 0 && _config.TaskRealSecondsFor(inputs.CrewTaskKind) is { } taskBudgetS)
        {
            leftS = inputs.CrewTaskSecondsLeftMet;
            budgetS = taskBudgetS;
            isTask = true;
        }
        else if (inputs.SecondsToHeldOrderMet is > 0 && _config.WaitRealSecondsMax is { } waitBudgetS)
        {
            leftS = inputs.SecondsToHeldOrderMet;
            budgetS = waitBudgetS;
        }
        else if (inputs.ParachutePhase && inputs.SecondsToDecisiveEventMet is { } toEventS &&
                 (_config.DescentRealSecondsMax ?? _config.WaitRealSecondsMax) is { } descentBudgetS)
        {
            // The fall under the canopy plays visibly fast-forwarded, in its own (longer) budget.
            leftS = toEventS - _config.EventLeadMetS;
            budgetS = descentBudgetS;
        }
        else if (_config.EntryRealSecondsMax is { } entryBudgetS && !inputs.ParachutePhase &&
                 inputs.SecondsToDecisiveEventMet is { } toDecisiveS && !(inputs.SecondsToNextEventMet <= toDecisiveS))
        {
            // The coast to the interface and the fall to the drogue (nothing else scheduled first, so not a burn's start).
            // A short stretch (two decisive moments a few seconds apart) still runs at no less than the second rung on average.
            leftS = toDecisiveS - _config.EventLeadMetS;
            budgetS = Math.Min(entryBudgetS, Math.Max(0, leftS.Value) / _config.Rates[Math.Min(1, TopRung)]);
        }
        if (leftS is { } wait && inputs.SecondsToDecisiveEventMet is { } decisiveS) leftS = Math.Min(wait, decisiveS - _config.EventLeadMetS);

        var blocked = hold is not (null or CompressionReason.Docking) || window is not null || inputs.BurnActive ||
                      inputs.AnyWorseningCondition || inputs.PlayerConfirmationPending || inputs.TutorialActionMaxRate is not null;
        if (blocked || leftS is not { } metLeftS || !(metLeftS > 0))
        {
            IsFastForward = false;
            return;
        }

        // A new wait starts a fresh budget, and so does the next task (its MET left jumps up); a descent's predicted gap may drift.
        if (!IsFastForward || isTask != _fastForwardIsTask || (isTask && metLeftS > _fastForwardLeftS + 1))
        {
            _fastForwardRealLeftS = budgetS;
            _fastForwardIsTask = isTask;
            _fastForwardRate = 1;
        }
        IsFastForward = true;
        _fastForwardLeftS = metLeftS;

        // As in the recovery wait, a re-check inside a frame keeps the frame's rate, and a frame that fell behind is made up by
        // the next ones; once the budget is down to one frame, that frame asks for the rest of the wait.
        if (realDeltaS > 0)
        {
            _fastForwardRate = 2 * metLeftS / Math.Max(_fastForwardRealLeftS, realDeltaS);
            _fastForwardRealLeftS -= realDeltaS;
        }

        if (_fastForwardRate > TargetRate)
        {
            TargetRate = _fastForwardRate;
            RuleRate = Math.Max(RuleRate, _fastForwardRate);
            if (isTask) Reason = CompressionReason.CrewTaskRunning;
        }
    }

    /// <summary>
    /// The descent under the chutes as one time-lapse (<see cref="DescentTimeLapse"/>) while a splashdown is predicted and
    /// nothing needs the player: an alarm, warning, burn, dangerous spin, confirmation or tutorial step waiting for them hands the
    /// rate back to the normal rules, and the time-lapse resumes with the budget it had left.
    /// </summary>
    /// <returns>True while the time-lapse sets the rate.</returns>
    private bool UpdateDescentTimeLapse(in CompressionInputs inputs, double realDeltaS, CompressionReason? hold)
    {
        if (!inputs.ParachutePhase) _descent.Reset();
        var applies = inputs.ParachutePhase && inputs.SecondsToSplashdownMet is > 0 &&
                      (_config.DescentRealSecondsMax ?? _config.WaitRealSecondsMax) is not null &&
                      hold is null && !inputs.BurnActive && !inputs.AnyWorseningCondition && !inputs.PlayerConfirmationPending &&
                      inputs.TutorialActionMaxRate is null;
        if (!applies)
        {
            // The fast-forward picks up afresh rather than from a budget the time-lapse left stale.
            if (IsFastForward && _descentRan) IsFastForward = false;
            _descentRan = false;
            return false;
        }

        var budgetS = (_config.DescentRealSecondsMax ?? _config.WaitRealSecondsMax)!.Value;
        var rate = _descent.Rate(inputs.SecondsToSplashdownMet!.Value, DeployMomentMetS(inputs), realDeltaS, budgetS, _config);
        _descentRan = true;
        SetRate(rate);
        _rung = CeilingRung(rate);
        _calmRealS = 0;
        Reason = CompressionReason.ParachutePhase;
        IsFastForward = true;
        // As before, only the guided run offers "Advance to next event" on the way down.
        UpdateAdvance(inputs, IsGuidedPacing);
        return true;
    }

    /// <summary>MET to the next chute's deploy moment or since the last decisive one (the drogue or the main), whichever is nearer.</summary>
    private static double DeployMomentMetS(in CompressionInputs inputs)
    {
        var moment = double.PositiveInfinity;
        if (inputs.SecondsToChuteDeployMet is { } toChute && toChute > 0) moment = toChute;
        if (inputs.SecondsSinceDecisiveEventMet is { } since && since >= 0) moment = Math.Min(moment, since);
        return moment;
    }

    /// <summary>The next decisive event is a chute's deploy moment, eased into instead of held at 1x.</summary>
    private static bool ChuteIsNextDecisive(in CompressionInputs inputs) =>
        inputs.SecondsToChuteDeployMet is { } toChute && inputs.SecondsToDecisiveEventMet is { } toDecisive && toDecisive >= toChute - 1e-3;

    private static MissionTime? HeldOrderStop(in CompressionInputs inputs)
    {
        if (inputs.SecondsToHeldOrderMet is not { } heldS || !(heldS >= 0) || double.IsInfinity(heldS)) return null;
        var markMs = inputs.Now.Milliseconds + (long)Math.Ceiling(heldS * 1000.0);
        var tick = SimulationClock.BaseTickMs;
        return new MissionTime((markMs + tick - 1) / tick * tick);
    }

    /// <summary>
    /// The recovery wait runs in <see cref="TimeCompressionConfig.RecoveryWaitRealSeconds"/> of real time: the rate is the
    /// remaining wait over the remaining real budget, so MET moves linearly in real time and a frame the host could not finish
    /// is made up by later ones. It overrides every other rule (nothing can be done afloat but wait); a freeze still stops it.
    /// </summary>
    /// <returns>True while the wait rule is in force.</returns>
    private bool UpdateRecoveryWait(in CompressionInputs inputs, double realDeltaS)
    {
        if (_config.RecoveryWaitRealSeconds is not { } budgetS || inputs.SecondsToRecoveryMet is not { } leftMetS || !(leftMetS > 0))
        {
            if (!IsRecoveryWait) return false;
            // Leaving the wait (recovered): continue from the top of the ladder, not from a rate in the thousands, so the mission's
            // end (a MET second later) does not cost a real second; the normal rules take over from there.
            IsRecoveryWait = false;
            _rung = TopRung;
            _calmRealS = 0;
            SetRate(_config.Rates[TopRung]);
            return false;
        }

        if (!IsRecoveryWait)
        {
            IsRecoveryWait = true;
            _recoveryRealLeftS = budgetS;
        }

        // Re-checks inside a frame (zero real delta) keep the frame's rate, so the host does not scale its own target down.
        // Once the budget is down to one frame (or spent), that frame asks for the whole rest of the wait.
        if (realDeltaS > 0)
        {
            SetRate(Math.Max(1, leftMetS / Math.Max(_recoveryRealLeftS, realDeltaS)));
            _recoveryRealLeftS -= realDeltaS;
        }

        _rung = TopRung;
        _calmRealS = 0;
        Reason = CompressionReason.StableCoast;
        CanAdvanceToNextEvent = false;
        NextEventAdvanceTarget = null;
        return true;
    }

    private void SetRate(double rate)
    {
        RuleRate = rate;
        TargetRate = rate;
        _smoothRate = rate;
        _blendFromRate = rate;
        _blendToRate = rate;
        _blendRealS = 0;
    }

    private void DetectEdges(in CompressionInputs inputs)
    {
        if (_hasPrevious)
        {
            if (_wasBurning && !inputs.BurnActive) OpenWindow(inputs.Now, CompressionReason.BurnInProgress);
            if (_wasDocking && !inputs.DockingActive) OpenWindow(inputs.Now, CompressionReason.Docking);
            if (!_wasBelowInterface && inputs.BelowEntryInterface) OpenWindow(inputs.Now, CompressionReason.EntryBelowInterface);
            // With a splashdown prediction the drogue's moment is the time-lapse's ease out of it, not a 1x window.
            if (!_wasUnderChutes && inputs.ParachutePhase && inputs.SecondsToSplashdownMet is null) OpenWindow(inputs.Now, CompressionReason.ParachutePhase);
            if (_wasBelowInterface && !inputs.BelowEntryInterface) OpenWindow(inputs.Now, CompressionReason.ParachutePhase);
        }

        _hasPrevious = true;
        _wasBurning = inputs.BurnActive;
        _wasDocking = inputs.DockingActive;
        _wasBelowInterface = inputs.BelowEntryInterface;
        _wasUnderChutes = inputs.ParachutePhase;
        TrackGSlope(inputs);
    }

    /// <summary>Load slope over at least one MET second: frames that advance a single tick may not see a new flight step.</summary>
    private void TrackGSlope(in CompressionInputs inputs)
    {
        if (!inputs.BelowEntryInterface)
        {
            _peakGSinceMs = long.MinValue;
            _gSampleMs = long.MinValue;
            _gSlopeGPerS = double.NaN;
            return;
        }

        var now = inputs.Now.Milliseconds;
        if (_gSampleMs == long.MinValue)
        {
            _gSampleMs = now;
            _gSample = inputs.GLoad;
            return;
        }

        if (now - _gSampleMs < GSlopeSpanMs) return;
        _gSlopeGPerS = (inputs.GLoad - _gSample) * 1000.0 / (now - _gSampleMs);
        _gSampleMs = now;
        _gSample = inputs.GLoad;
    }

    private void OpenWindow(MissionTime now, CompressionReason reason)
    {
        _windowUntilMs = Math.Max(_windowUntilMs, now.Milliseconds + (long)Math.Round(_config.EventLagMetS * 1000.0));
        _windowReason = reason;
    }

    private CompressionReason? HoldReason(in CompressionInputs inputs)
    {
        if (inputs.CriticalAlarmUnacknowledged) return CompressionReason.CriticalAlarm;
        if (inputs.RapidlyWorsening) return CompressionReason.WorseningCondition;
        if (!(Math.Abs(inputs.MaxAngularRateDps) < _config.DangerousAngularRateDps)) return CompressionReason.HighAngularRate;
        if (inputs.BurnActive && _config.BurnMaxRate <= 1) return CompressionReason.BurnInProgress;
        if (inputs.DockingActive) return CompressionReason.Docking;
        return null;
    }

    private CompressionReason? WindowReason(in CompressionInputs inputs)
    {
        if (inputs.Now.Milliseconds < _windowUntilMs) return _windowReason;
        if (inputs.BelowEntryInterface && inputs.GLoad >= _config.PeakGMinG && Math.Abs(_gSlopeGPerS) <= _config.PeakGMaxSlopeGPerS &&
            PeakGHoldLeft(inputs.Now))
            return CompressionReason.EntryBelowInterface;
        var nearDecisive = (inputs.SecondsToDecisiveEventMet <= _config.EventLeadMetS && !ChuteIsNextDecisive(inputs)) ||
                           inputs.SecondsSinceDecisiveEventMet < _config.EventLagMetS;
        if (!nearDecisive) return null;
        return inputs.ParachutePhase ? CompressionReason.ParachutePhase
            : inputs.BelowEntryInterface ? CompressionReason.EntryBelowInterface
            : CompressionReason.EventApproaching;
    }

    /// <summary>The peak-g hold has time left this entry (<see cref="TimeCompressionConfig.PeakGHoldMetS"/>); starts its clock.</summary>
    private bool PeakGHoldLeft(MissionTime now)
    {
        if (_config.PeakGHoldMetS is not { } holdS) return true;
        if (_peakGSinceMs == long.MinValue) _peakGSinceMs = now.Milliseconds;
        return now.Milliseconds - _peakGSinceMs < holdS * 1000.0;
    }

    private (double Cap, CompressionReason Reason) ConditionCap(in CompressionInputs inputs)
    {
        var cap = double.PositiveInfinity;
        var reason = CompressionReason.StableCoast;
        void Consider(bool applies, double rate, CompressionReason why)
        {
            if (applies && rate < cap)
            {
                cap = rate;
                reason = why;
            }
        }

        Consider(Math.Abs(inputs.MaxAngularRateDps) > _config.MaxStableAngularRateDps, _config.SpinMaxRate, CompressionReason.HighAngularRate);
        Consider(inputs.BurnActive, _config.BurnMaxRate, CompressionReason.BurnInProgress);
        var predicted = inputs.SecondsToDecisiveEventMet is not null && _config.PredictedDescentMaxRate is not null;
        var predictedRate = _config.PredictedDescentMaxRate ?? 1;
        Consider(inputs.ParachutePhase, predicted ? Math.Max(_config.ParachuteMaxRate, predictedRate) : _config.ParachuteMaxRate, CompressionReason.ParachutePhase);
        Consider(inputs.BelowEntryInterface && !inputs.ParachutePhase, predicted ? Math.Max(_config.EntryMaxRate, predictedRate) : _config.EntryMaxRate, CompressionReason.EntryBelowInterface);
        Consider(inputs.AnyWorseningCondition, _config.WorseningMaxRate, CompressionReason.WorseningCondition);
        Consider(inputs.CrewTaskRunning, _config.TaskRunningMaxRate, CompressionReason.CrewTaskRunning);
        Consider(inputs.CalmOpening, _config.OpeningMaxRate, CompressionReason.StableCoast);
        Consider(inputs.TipEscalationPending, _config.TipEscalationMaxRate, CompressionReason.StableCoast);
        if (inputs.TutorialActionMaxRate is { } tutorialRate) Consider(true, Math.Max(1, tutorialRate), CompressionReason.StableCoast);
        return (cap, reason);
    }

    private double ApproachCap(in CompressionInputs inputs)
    {
        var cap = double.PositiveInfinity;
        if (ChuteIsNextDecisive(inputs)) cap = Math.Min(cap, Math.Max(_config.DescentSlowRate, inputs.SecondsToChuteDeployMet!.Value / _config.DescentEaseRealS));
        else if (inputs.SecondsToDecisiveEventMet is { } toDecisive) cap = Math.Min(cap, Approach(toDecisive, 1));
        if (inputs.SecondsToNextEventMet is { } toNext) cap = Math.Min(cap, Approach(toNext, _config.ScheduledEventFloorRate));
        return cap;
    }

    private double Approach(double secondsToEventMet, double floorRate)
    {
        var gapS = secondsToEventMet - _config.EventLeadMetS;
        if (!(gapS > 0)) return floorRate;
        if (_config.EventApproachRealS <= 0) return double.PositiveInfinity;
        return Math.Max(floorRate, gapS / _config.EventApproachRealS);
    }

    private bool IsSteady(in CompressionInputs inputs) =>
        !inputs.AnyWorseningCondition &&
        !inputs.BelowEntryInterface &&
        !inputs.ParachutePhase &&
        Math.Abs(inputs.MaxAngularRateDps) <= _config.MaxStableAngularRateDps;

    private void UpdateAdvance(in CompressionInputs inputs, bool calm)
    {
        var next = inputs.SecondsToNextEventMet;
        if (inputs.SecondsToDecisiveEventMet is { } decisive && !(next <= decisive)) next = decisive;
        // A quiet coast with nothing ahead may still be skipped, up to the open span; anything that needs the player stops it.
        var advanceGapS = next is null ? _config.AdvanceOpenMetS : next - _config.EffectiveAdvanceLeadMetS;
        NextEventAdvanceTarget = advanceGapS > 0
            ? new MissionTime(inputs.Now.Milliseconds + (long)Math.Round(advanceGapS.Value * 1000.0))
            : null;
        CanAdvanceToNextEvent = NextEventAdvanceTarget is not null && calm;
    }

    private void Climb(int cap, double realDeltaS)
    {
        if (_rung >= cap)
        {
            _rung = cap;
            _calmRealS = 0;
            return;
        }

        if (_config.StableRealSecondsPerStep <= 0)
        {
            _rung = cap;
            return;
        }

        _calmRealS += realDeltaS;
        while (_rung < cap && _calmRealS >= _config.StableRealSecondsPerStep)
        {
            _rung++;
            _calmRealS -= _config.StableRealSecondsPerStep;
        }
        if (_rung == cap) _calmRealS = 0;
    }

    private void Blend(double realDeltaS, bool snap)
    {
        if (snap)
        {
            _smoothRate = RuleRate;
            _blendFromRate = RuleRate;
            _blendToRate = RuleRate;
            _blendRealS = 0;
            return;
        }

        if (RuleRate != _blendToRate)
        {
            _blendFromRate = _smoothRate;
            _blendToRate = RuleRate;
            _blendRealS = 0;
        }

        _blendRealS += realDeltaS;
        var fraction = _config.SmoothingRealS <= 0 ? 1 : Math.Min(1, _blendRealS / _config.SmoothingRealS);
        _smoothRate = fraction >= 1
            ? _blendToRate
            : Math.Exp(Math.Log(_blendFromRate) + ((Math.Log(_blendToRate) - Math.Log(_blendFromRate)) * fraction));
    }

    /// <summary>The lowest rung at or above <paramref name="rate"/> (the top rung when none is).</summary>
    private int CeilingRung(double rate)
    {
        for (var rung = 0; rung < TopRung; rung++)
        {
            if (_config.Rates[rung] >= rate) return rung;
        }
        return TopRung;
    }
}
