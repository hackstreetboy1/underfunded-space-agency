using System.Globalization;
using Usa.Game.Content;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>
/// The burn planner panel (UI.md 4.1, GDD 7.2): a draft burn, its validation, a throttled preview through
/// <see cref="IManeuverPreviewPort"/> with formatted numbers and warnings, and on Easy the "Propose burn" assist.
/// Edits mark the preview stale; the host calls <see cref="Update"/> each frame and the preview reruns at most once
/// per <see cref="BurnPlannerSettings.PreviewIntervalS"/>; time-relative words (<c>Splashdown in 12 min</c>, stale ages)
/// are re-derived from MET on every <see cref="Update"/>, so the host need not invalidate for the clock. Executing the
/// plan is the host's job (crew order or remote command) via <see cref="BuildPlan"/>.
/// Information is earned (X8, GDD 4.1-4.2): previews, proposals and every displayed propellant figure come from
/// <see cref="PlannerEngineOption.Propellant"/>, what the ground knows, never from the engine's true load; only
/// <see cref="BuildPlan"/> carries the real engine for execution.
/// Proposals run through an <see cref="IProposalScheduler"/>: the flight picture is captured when the player asks, the
/// work may run on a worker while <see cref="ProposalPending"/> shows it is under way, and <see cref="Update"/> applies
/// the result on the thread driving the model. A new request or an edit to the draft replaces or drops a pending one.
/// </summary>
public sealed class BurnPlannerModel
{
    /// <summary>Starts this far in the past are treated as "now" without a warning (clock jitter).</summary>
    private const long StartGraceMs = 1000;

    private readonly IManeuverPreviewPort _previewPort;
    private readonly IBurnProposalPort? _proposalPort;
    private readonly IProposalScheduler _proposalScheduler;
    private readonly Func<TimeSpan> _clock;
    private readonly Func<string, string> _zoneLabel;
    private readonly MapText _text;
    private readonly ProposalText _proposalText;
    private readonly TimeSpan _previewInterval;
    private IReadOnlyList<PlannerEngineOption> _engines = [];
    private TimeSpan? _lastPreviewAt;
    private bool _durationClamped;
    private long _textMetSeconds;
    private PendingProposal? _pendingProposal;

    /// <param name="clock">Monotonic real time (e.g. <c>() =&gt; monotonicClock.Elapsed</c>), for preview throttling.</param>
    /// <param name="text">Planner and map words (<c>planner.*</c>, <c>map.*</c> strings).</param>
    /// <param name="proposalPort">Required for <see cref="ProposeBurn"/>; Normal difficulty passes null.</param>
    /// <param name="proposalScheduler">
    /// Where proposals run; the game passes a <see cref="ThreadPoolProposalScheduler"/>. Defaults to
    /// <see cref="InlineProposalScheduler"/>: computed inside <see cref="ProposeBurn"/>, as headless runs and tests expect.
    /// </param>
    public BurnPlannerModel(
        IManeuverPreviewPort previewPort, Func<TimeSpan> clock, ITextSource text, BurnPlannerSettings? settings = null,
        IBurnProposalPort? proposalPort = null, Func<string, string>? zoneLabel = null, IProposalScheduler? proposalScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(previewPort);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(text);
        _previewPort = previewPort;
        _clock = clock;
        _text = new MapText(text);
        _proposalText = new ProposalText(_text);
        _proposalPort = proposalPort;
        _proposalScheduler = proposalScheduler ?? InlineProposalScheduler.Instance;
        Settings = settings ?? new BurnPlannerSettings();
        _zoneLabel = zoneLabel ?? RecoveryZoneGeometry.DefaultLabel;
        _previewInterval = TimeSpan.FromSeconds(Settings.PreviewIntervalS);
        Issues = Validate();
    }

    /// <summary>Raised after any change to the draft, issues, preview or proposal.</summary>
    public event EventHandler? Changed;

    public BurnPlannerSettings Settings { get; }

    public IReadOnlyList<PlannerEngineOption> Engines => _engines;

    public BurnDraft Draft { get; private set; } = BurnDraft.Empty;

    public PlannerConditions Conditions { get; private set; } = PlannerConditions.Nominal;

    public PlannerEngineOption? SelectedEngine => Find(Draft.EngineId);

    /// <summary>Validation of the draft; blocking entries disable Execute.</summary>
    public IReadOnlyList<PlannerMessage> Issues { get; private set; }

    /// <summary>Warnings about the previewed outcome (and vehicle conditions).</summary>
    public IReadOnlyList<PlannerMessage> Warnings { get; private set; } = [];

    public ManeuverPreview? Preview { get; private set; }

    public BurnPreviewText? PreviewText { get; private set; }

    /// <summary>The draft changed since the shown preview was computed.</summary>
    public bool PreviewPending { get; private set; }

    public ProposalOutcome? LastProposal { get; private set; }

    /// <summary>A proposal is being computed (show it as under way); <see cref="Update"/> applies it when it is ready.</summary>
    public bool ProposalPending => _pendingProposal is not null;

    /// <summary>What the pending proposal is for; null when none is pending.</summary>
    public BurnGoal? PendingGoal => _pendingProposal?.Goal;

    /// <summary>Execute needs a valid draft and a preview of exactly that draft on screen.</summary>
    public bool CanExecute => !HasBlockingIssue() && !PreviewPending && _previewPort.CanPreview;

    public bool CanPropose => Settings.ProposalsEnabled && _proposalPort is not null && SelectedEngine is { Available: true, Engine: not null };

    /// <summary>Replaces the engine list, keeping the selection when it is still offered.</summary>
    public void SetEngines(IReadOnlyList<PlannerEngineOption> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        _engines = engines;
        if (Find(Draft.EngineId) is null) Draft = Draft with { EngineId = null };
        else if (DirectionLocked) Draft = Draft with { Direction = BurnDirection.Retrograde, CustomAngleDeg = 0 };
        if (!CanPropose) DropProposal();
        DraftChanged();
    }

    /// <summary>
    /// The selected engine fires in one direction only: the retro sequence always fires retrograde (the retrofire
    /// task), so the draft is set to retrograde and direction edits are ignored.
    /// </summary>
    public bool DirectionLocked => SelectedEngine?.Engine is RetroSequenceEngine;

    /// <summary>
    /// The fixed firing pattern under the duration row (Gate 4 rev 2), e.g. <c>4 × 5.5 s at 0 / 4 / 8 / 12 s</c>;
    /// null for engines with a manual duration.
    /// </summary>
    public string? FixedDurationDetail => SelectedEngine?.Engine is RetroSequenceEngine retro ? RetroPattern(retro.Sequence) : null;

    public void SelectEngine(string engineId)
    {
        ArgumentException.ThrowIfNullOrEmpty(engineId);
        if (Find(engineId) is not { } option) throw new ArgumentException($"Unknown engine '{engineId}'.", nameof(engineId));
        DropProposal();
        Draft = option.Engine is RetroSequenceEngine
            ? Draft with { EngineId = engineId, Direction = BurnDirection.Retrograde, CustomAngleDeg = 0 }
            : Draft with { EngineId = engineId };
        DraftChanged();
    }

    /// <summary>Ignored while <see cref="DirectionLocked"/>.</summary>
    public void SetDirection(BurnDirection direction)
    {
        if (DirectionLocked) return;
        DropProposal();
        Draft = Draft with { Direction = direction };
        DraftChanged();
    }

    /// <summary>Sets a free angle (degrees from prograde towards radial-out) and selects <see cref="BurnDirection.Custom"/>; ignored while <see cref="DirectionLocked"/>.</summary>
    public void SetCustomAngle(double degrees)
    {
        if (!double.IsFinite(degrees) || DirectionLocked) return;
        DropProposal();
        Draft = Draft with { Direction = BurnDirection.Custom, CustomAngleDeg = Angles.Wrap(degrees * Angles.DegToRad) * Angles.RadToDeg };
        DraftChanged();
    }

    /// <summary>Sets the manual burn duration, snapped to the duration step and clamped to [0, max].</summary>
    public void SetDuration(double seconds)
    {
        if (!double.IsFinite(seconds)) return;
        DropProposal();
        var snapped = Math.Round(seconds / Settings.DurationStepS) * Settings.DurationStepS;
        var clamped = Math.Clamp(snapped, 0, Settings.MaxDurationS);
        _durationClamped = snapped > Settings.MaxDurationS;
        Draft = Draft with { DurationS = Math.Round(clamped, 6) };
        DraftChanged();
    }

    /// <summary>Start at the maneuver node's time; null starts now.</summary>
    public void SetStartTime(MissionTime? start)
    {
        DropProposal();
        Draft = Draft with { StartTime = start, StartMode = BurnStartMode.Node, StartZoneId = null };
        DraftChanged();
    }

    /// <summary>Start at the next pass over <paramref name="zoneId"/> (time from the map's zone-pass prediction).</summary>
    public void SetStartAtZonePass(string zoneId, MissionTime passTime)
    {
        ArgumentException.ThrowIfNullOrEmpty(zoneId);
        DropProposal();
        Draft = Draft with { StartTime = passTime, StartMode = BurnStartMode.ZonePass, StartZoneId = zoneId };
        DraftChanged();
    }

    /// <summary>
    /// The direction row's label (Figma G4-02): the draft's direction and how the burn is held, <c>Retrograde · RCS B hold</c>,
    /// <c>Custom 200° · no hold</c> without attitude control (custom angles read 0–360° like the angle field), or the bare direction when the ground knows no hold.
    /// </summary>
    public string DirectionLabel
    {
        get
        {
            var direction = Draft.Direction switch
            {
                BurnDirection.Prograde => _text.Get("planner.directionPrograde"),
                BurnDirection.Retrograde => _text.Get("planner.directionRetrograde"),
                BurnDirection.RadialIn => _text.Get("planner.directionRadialIn"),
                BurnDirection.RadialOut => _text.Get("planner.directionRadialOut"),
                _ => _text.Format("planner.directionCustom",
                    ("angle", string.Create(CultureInfo.InvariantCulture, $"{Math.Round(((Draft.CustomAngleDeg % 360) + 360) % 360, MidpointRounding.AwayFromZero) % 360:0}°"))),
            };
            if (!Conditions.AttitudeControlAvailable) return _text.Format("planner.directionNoHold", ("direction", direction));
            return Conditions.Hold is { } hold
                ? _text.Format("planner.directionHold", ("direction", direction), ("system", hold.SystemLabel))
                : direction;
        }
    }

    /// <summary>Where the hold label comes from, <c>Brandt · MET 07:12</c>; null when the ground knows no hold.</summary>
    public string? HoldMeta => Conditions.Hold is { } hold
        ? _text.Format("planner.holdMeta", ("source", hold.Source), ("time", FlightFormat.MetShort(hold.At)))
        : null;

    public void SetConditions(PlannerConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        if (conditions == Conditions) return;
        Conditions = conditions;
        DraftChanged();
    }

    /// <summary>"Clear": back to an empty draft (engine list kept) and no preview.</summary>
    public void Clear()
    {
        DropProposal();
        Draft = BurnDraft.Empty;
        _durationClamped = false;
        Preview = null;
        PreviewText = null;
        Warnings = [];
        LastProposal = null;
        PreviewPending = false;
        Issues = Validate();
        OnChanged();
    }

    /// <summary>The flight picture changed (new state vector, docking...): rerun the preview at the next <see cref="Update"/>.</summary>
    public void Invalidate()
    {
        if (Preview is null && !CanBuildPlan()) return;
        PreviewPending = true;
    }

    /// <summary>
    /// Applies a proposal that has finished computing, then runs the pending preview if the throttle interval has
    /// passed, else refreshes MET-relative words.
    /// </summary>
    /// <returns>True when a preview ran.</returns>
    public bool Update()
    {
        if (_pendingProposal is { Work.IsCompleted: true }) CompleteProposal();
        if (!PreviewPending)
        {
            RefreshTimeWords();
            return false;
        }
        var now = _clock();
        if (_lastPreviewAt is { } last && now - last < _previewInterval) return false;
        RunPreview(now);
        return true;
    }

    /// <summary>The plan to execute, or null while a blocking issue remains.</summary>
    public ManeuverPlan? BuildPlan() => HasBlockingIssue() ? null : PlanFromDraft(forPreview: false);

    /// <summary>
    /// Easy: asks the proposer for <paramref name="goal"/> with the selected engine, replacing any proposal still
    /// computing; the draft is filled when it finds a burn.
    /// </summary>
    /// <returns>
    /// The outcome when the proposal is ready at once (the inline scheduler); null when the assist is unavailable or the
    /// proposal is computing (<see cref="ProposalPending"/>), in which case <see cref="Update"/> applies it later and sets
    /// <see cref="LastProposal"/>.
    /// </returns>
    public ProposalOutcome? ProposeBurn(BurnGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (!CanPropose || _proposalPort is null || SelectedEngine is not { Engine: not null } option) return null;

        DropProposal();
        var earliest = Draft.StartTime is { } start && start > _previewPort.Now ? start : _previewPort.Now;
        Func<BurnProposal> work;
        try
        {
            work = _proposalPort.PrepareProposal(goal, PlannerPropellant.ForPlanning(option, Settings.MaxDurationS), earliest);
        }
        catch (PreviewUnavailableException)
        {
            return ApplyProposal(goal, Unavailable(goal));
        }

        var cancellation = new CancellationTokenSource();
        _pendingProposal = new PendingProposal(goal, _proposalScheduler.Run(work, cancellation.Token), cancellation);
        if (_pendingProposal.Work.IsCompleted) return CompleteProposal();
        OnChanged();
        return null;
    }

    /// <summary>Forgets a proposal still computing; its result, when it comes, is dropped.</summary>
    private void DropProposal()
    {
        if (_pendingProposal is not { } pending) return;
        _pendingProposal = null;
        pending.Cancellation.Cancel();
        pending.Cancellation.Dispose();
        // Observe a late failure so it is not reported as unobserved; the result itself is no longer wanted.
        pending.Work.ContinueWith(
            static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Applies the finished pending proposal; a vanished vehicle is an explained failure, anything else a bug and rethrown.</summary>
    private ProposalOutcome CompleteProposal()
    {
        var pending = _pendingProposal!;
        _pendingProposal = null;
        pending.Cancellation.Dispose();
        BurnProposal proposal;
        try
        {
            proposal = pending.Work.GetAwaiter().GetResult();
        }
        catch (PreviewUnavailableException)
        {
            proposal = Unavailable(pending.Goal);
        }
        return ApplyProposal(pending.Goal, proposal);
    }

    private static BurnProposal Unavailable(BurnGoal goal) => new(goal, null, null, BurnProposer.KeyUnavailable, []);

    private ProposalOutcome ApplyProposal(BurnGoal goal, BurnProposal proposal)
    {
        var explanation = _proposalText.Explain(proposal, _zoneLabel);
        if (proposal.Plan is { } plan)
        {
            Draft = Draft with
            {
                Direction = plan.Direction,
                CustomAngleDeg = plan.CustomAngleDeg,
                DurationS = plan.EffectiveDurationS,
                StartTime = plan.StartTime,
                StartMode = BurnStartMode.Node,
                StartZoneId = null,
            };
            _durationClamped = false;
            Issues = Validate();
            if (proposal.Preview is { } preview && Conditions.AssumedAttitudeErrorDeg == 0)
            {
                ShowPreview(preview);
                _lastPreviewAt = _clock();
                PreviewPending = false;
            }
            else
            {
                PreviewPending = true;
            }
        }

        LastProposal = new ProposalOutcome(goal, proposal.HasPlan, proposal.ExplanationKey, explanation);
        OnChanged();
        return LastProposal;
    }

    private void DraftChanged()
    {
        Issues = Validate();
        PreviewPending = true;
        if (!CanBuildPlan())
        {
            Preview = null;
            PreviewText = null;
            Warnings = ConditionWarnings();
            PreviewPending = false;
        }
        OnChanged();
    }

    private void RunPreview(TimeSpan now)
    {
        _lastPreviewAt = now;
        PreviewPending = false;
        var plan = PlanFromDraft(forPreview: true);
        if (plan is null || !_previewPort.CanPreview)
        {
            ClearPreview();
            return;
        }

        ManeuverPreview preview;
        try
        {
            preview = _previewPort.Preview(plan);
        }
        catch (PreviewUnavailableException)
        {
            ClearPreview();
            return;
        }
        ShowPreview(preview);
        Issues = Validate();
        OnChanged();
    }

    private void ClearPreview()
    {
        Preview = null;
        PreviewText = null;
        Warnings = ConditionWarnings();
        Issues = Validate();
        OnChanged();
    }

    private void ShowPreview(ManeuverPreview preview)
    {
        Preview = preview;
        Warnings = PreviewWarnings(preview);
        FormatPreview();
    }

    private void FormatPreview()
    {
        if (Preview is not { } preview) return;
        var now = _previewPort.Now;
        _textMetSeconds = now.Milliseconds / 1000;
        PreviewText = BurnPreviewText.From(preview, now, _zoneLabel, Warnings, _text, PropellantFiguresOf(preview, now));
    }

    private void RefreshTimeWords()
    {
        if (Preview is null || _previewPort.Now.Milliseconds / 1000 == _textMetSeconds) return;
        FormatPreview();
        OnChanged();
    }

    /// <param name="forPreview">Preview and display fly the ground's known propellant; execution flies the real engine.</param>
    private ManeuverPlan? PlanFromDraft(bool forPreview)
    {
        if (!CanBuildPlan() || SelectedEngine is not { Engine: { } engine } option) return null;
        var now = _previewPort.Now;
        var start = Draft.StartTime is { } requested && requested > now ? requested : now;
        var flown = forPreview ? PlannerPropellant.ForPlanning(option, Draft.DurationS) : engine;
        return new ManeuverPlan(flown, Draft.Direction, Draft.DurationS, start, Draft.CustomAngleDeg, Conditions.AssumedAttitudeErrorDeg);
    }

    private PropellantFigures? PropellantFiguresOf(ManeuverPreview preview, MissionTime now)
    {
        if (SelectedEngine is not { Engine: ConstantThrustEngine engine, KnownPropellant: { } known }) return null;
        var planned = PlannerPropellant.PlannedUseKg(engine, preview.Plan.EffectiveDurationS);
        // Mass without this engine's propellant is preview physics (X8), never shown: the ground ledger's mass less what the
        // ground believes is in this tank, both current knowledge (never the option's cached true load).
        var massWithoutTank = _previewPort.MassBand.BestKg - (known.HasValue ? (known.LowKg + known.HighKg) / 2 : 0);
        var ve = engine.ExhaustVelocityMps;
        double RemainingDv(double onBoardKg) =>
            PlannerPropellant.RemainingDeltaVMps(ve, massWithoutTank, onBoardKg, Math.Min(planned, onBoardKg));

        var meta = known.Source is { } source
            ? known.ObservedAt is { } at ? _text.Format("planner.propellantMeta", ("source", source), ("time", FlightFormat.MetShort(at))) : source
            : null;
        return known.State switch
        {
            PlannerKnowledgeState.Known or PlannerKnowledgeState.Stale when known.HasValue => new PropellantFigures(
                FlightFormat.Mass(Math.Min(planned, known.HighKg)),
                FlightFormat.Speed(RemainingDv(known.HighKg)),
                known.State == PlannerKnowledgeState.Stale && known.ObservedAt is { } observed
                    ? _text.Format("planner.badgeStale", ("age", FlightFormat.Age((now.Milliseconds - observed.Milliseconds) / 1000.0)))
                    : string.Empty,
                meta,
                known.State),
            PlannerKnowledgeState.Estimated when known.HasValue => new PropellantFigures(
                FlightFormat.MassRange(Math.Min(planned, known.LowKg), Math.Min(planned, known.HighKg)),
                FlightFormat.SpeedRange(RemainingDv(known.LowKg), RemainingDv(known.HighKg)),
                _text.Get("planner.badgeEstimated"),
                meta,
                known.State),
            _ => new PropellantFigures(
                FlightFormat.Mass(planned), FlightFormat.Placeholder, _text.Get("planner.badgeUnknown"), meta, PlannerKnowledgeState.Unknown),
        };
    }

    private bool CanBuildPlan() =>
        SelectedEngine is { Available: true, Engine: { } engine } && (engine.FixedDurationS is not null || Draft.DurationS > 0);

    private bool HasBlockingIssue()
    {
        foreach (var issue in Issues)
        {
            if (issue.Blocking) return true;
        }
        return false;
    }

    private PlannerEngineOption? Find(string? engineId)
    {
        if (engineId is null) return null;
        foreach (var option in _engines)
        {
            if (option.Id == engineId) return option;
        }
        return null;
    }

    private List<PlannerMessage> Validate()
    {
        var issues = new List<PlannerMessage>();
        var engine = SelectedEngine;
        if (engine is null)
        {
            issues.Add(new PlannerMessage(PlannerMessageKind.NoEngine, PlannerSeverity.Critical, _text.Get("planner.selectEngine"), Blocking: true));
        }
        else if (!engine.Available || engine.Engine is null)
        {
            issues.Add(new PlannerMessage(PlannerMessageKind.EngineUnavailable, PlannerSeverity.Critical,
                engine.UnavailableReason is { } reason
                    ? _text.Format("planner.engineUnavailableReason", ("engine", engine.Label), ("reason", reason))
                    : _text.Format("planner.engineUnavailable", ("engine", engine.Label)),
                Blocking: true));
        }
        else if (engine.Engine.FixedDurationS is { } fixedS)
        {
            issues.Add(new PlannerMessage(PlannerMessageKind.FixedDuration, PlannerSeverity.Info,
                _text.Format("planner.fixedDuration", ("engine", engine.Label), ("duration", FlightFormat.Duration(fixedS)))));
        }
        else if (Draft.DurationS <= 0)
        {
            issues.Add(new PlannerMessage(PlannerMessageKind.NoDuration, PlannerSeverity.Critical, _text.Get("planner.noDuration"), Blocking: true));
        }

        if (_durationClamped && engine?.HasFixedDuration != true)
        {
            issues.Add(new PlannerMessage(PlannerMessageKind.DurationClamped, PlannerSeverity.Caution,
                _text.Format("planner.durationClamped", ("duration", FlightFormat.Duration(Settings.MaxDurationS)))));
        }
        if (Draft.StartTime is { } start && start.Milliseconds < _previewPort.Now.Milliseconds - StartGraceMs)
        {
            issues.Add(new PlannerMessage(PlannerMessageKind.StartInPast, PlannerSeverity.Caution, _text.Get("planner.startInPast")));
        }
        if (!_previewPort.CanPreview)
        {
            issues.Add(new PlannerMessage(PlannerMessageKind.PreviewUnavailable, PlannerSeverity.Caution,
                _text.Get("planner.previewUnavailable"), Blocking: true));
        }
        return issues;
    }

    private List<PlannerMessage> ConditionWarnings()
    {
        var warnings = new List<PlannerMessage>();
        if (!Conditions.AttitudeControlAvailable)
        {
            warnings.Add(new PlannerMessage(PlannerMessageKind.AttitudeControlUnavailable, PlannerSeverity.Caution,
                _text.Get("planner.attitudeUnavailable")));
        }
        return warnings;
    }

    private List<PlannerMessage> PreviewWarnings(ManeuverPreview preview)
    {
        var warnings = ConditionWarnings();
        AddPropellantWarning(warnings, preview);
        if (preview.Entry is { } entry)
        {
            if (entry.Corridor == CorridorQuality.TooSteep)
            {
                warnings.Add(new PlannerMessage(PlannerMessageKind.EntryTooSteep, PlannerSeverity.Critical,
                    _text.Format("planner.entryTooSteep", ("gLoad", FlightFormat.GLoad(entry.PeakGLoad)))));
            }
            else if (entry.Corridor == CorridorQuality.TooShallow)
            {
                warnings.Add(new PlannerMessage(PlannerMessageKind.EntryTooShallow, PlannerSeverity.Caution,
                    _text.Get("planner.entryTooShallow")));
            }

            if (entry.Landing is { } landing)
            {
                if (!landing.Zone.InZone)
                {
                    warnings.Add(new PlannerMessage(PlannerMessageKind.LandingOutsideZones, PlannerSeverity.Caution,
                        _text.Format("planner.landingOutsideZones", ("hours", FlightFormat.Hours(landing.Zone.RecoveryWaitHours)))));
                }
            }
            else
            {
                warnings.Add(new PlannerMessage(PlannerMessageKind.NoLandingInHorizon, PlannerSeverity.Caution,
                    _text.Get("planner.noLandingInHorizon")));
            }
        }
        return warnings;
    }

    /// <summary>Propellant warnings from what the ground knows (X8); the preview's own flag is knowledge-derived too.</summary>
    private void AddPropellantWarning(List<PlannerMessage> warnings, ManeuverPreview preview)
    {
        var known = SelectedEngine?.KnownPropellant;
        if (known is null)
        {
            if (preview.PropellantLimited)
            {
                warnings.Add(new PlannerMessage(PlannerMessageKind.PropellantLimited, PlannerSeverity.Caution,
                    _text.Format("planner.propellantLimited", ("mass", FlightFormat.Mass(preview.PropellantUsedKg)))));
            }
            return;
        }
        if (!known.HasValue)
        {
            warnings.Add(new PlannerMessage(PlannerMessageKind.PropellantUnknown, PlannerSeverity.Caution, _text.Get("planner.propellantUnknown")));
            return;
        }

        var planned = SelectedEngine?.Engine is { } engine ? PlannerPropellant.PlannedUseKg(engine, preview.Plan.EffectiveDurationS) : 0;
        if (planned > known.HighKg)
        {
            var used = known.State == PlannerKnowledgeState.Estimated
                ? FlightFormat.MassRange(known.LowKg, known.HighKg)
                : FlightFormat.Mass(known.HighKg);
            warnings.Add(new PlannerMessage(PlannerMessageKind.PropellantLimited, PlannerSeverity.Caution,
                _text.Format("planner.propellantLimited", ("mass", used))));
        }
        else if (planned > known.LowKg)
        {
            warnings.Add(new PlannerMessage(PlannerMessageKind.PropellantMayRunOut, PlannerSeverity.Caution,
                _text.Format("planner.propellantMayRunOut", ("range", FlightFormat.MassRange(known.LowKg, known.HighKg)))));
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private string? RetroPattern(RetroSequence sequence)
    {
        if (sequence.Rockets.Count == 0) return null;
        var rockets = sequence.Rockets.OrderBy(r => r.IgnitionOffsetS).ToArray();
        var burnS = rockets[0].BurnDurationS;
        if (Array.TrueForAll(rockets, r => Math.Abs(r.BurnDurationS - burnS) < 1e-9))
        {
            return _text.Format("planner.retroPattern",
                ("count", rockets.Length.ToString(CultureInfo.InvariantCulture)),
                ("burn", FlightFormat.Duration(burnS)),
                ("offsets", string.Join(" / ", rockets.Select(r => Seconds(r.IgnitionOffsetS)))));
        }
        return string.Join(" · ", rockets.Select(r =>
            _text.Format("planner.retroRocket", ("burn", FlightFormat.Duration(r.BurnDurationS)), ("offset", Seconds(r.IgnitionOffsetS)))));

        static string Seconds(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private sealed record PendingProposal(BurnGoal Goal, Task<BurnProposal> Work, CancellationTokenSource Cancellation);
}
