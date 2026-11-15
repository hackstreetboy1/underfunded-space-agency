using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>
/// Dynamic tips (GDD 9.1). Opens and retires authored problems from their expressions, tracks a stuck
/// score per open problem, and escalates the top-priority problem one stage at a time as the score and
/// cooldown allow. It only reads state and publishes <see cref="TipAvailable"/> / <see cref="TipWithdrawn"/>;
/// it never changes simulation state, never pauses time, and nothing it does reaches the grade (D13).
/// </summary>
public sealed class GuidanceDirector : ISimSystem, IHashContributor
{
    private readonly HintTree _tree;
    private readonly GuidanceSettings _settings;
    private readonly IStateQuery _state;
    private readonly EventBus _events;
    private readonly ProblemStatus[] _status;
    private readonly StuckTracker[] _trackers;
    private readonly HintStage[] _shown;
    private readonly long _cooldownMs;
    private long? _lastTipMs;
    private int _currentProblem = -1;

    public GuidanceDirector(HintTree tree, GuidanceSettings settings, IStateQuery state, EventBus events)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.EscalationS <= 0) throw new ArgumentException("Escalation time must be positive.", nameof(settings));
        _tree = tree;
        _settings = settings;
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        var count = tree.Problems.Count;
        _status = new ProblemStatus[count];
        _shown = new HintStage[count];
        _trackers = new StuckTracker[count];
        for (var i = 0; i < count; i++) _trackers[i] = new StuckTracker(tree.Stuck);
        _cooldownMs = (long)Math.Round(tree.CooldownS * 1000.0);
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    /// <summary>Set by the integration step when the crew slot, open reports, plans or critical alarms change.</summary>
    public GuidanceActivity Activity { get; set; }

    /// <summary>The tip currently on the card, or null.</summary>
    public TipAvailable? CurrentTip { get; private set; }

    public void Step(in StepContext context)
    {
        var problems = _tree.Problems;
        for (var i = 0; i < problems.Count; i++)
        {
            switch (_status[i])
            {
                case ProblemStatus.Inactive:
                    if (GuidanceConditions.Holds(problems[i].Activation, _state) && !GuidanceConditions.Holds(problems[i].Resolution, _state)) _status[i] = ProblemStatus.Open;
                    break;
                case ProblemStatus.Open:
                    if (GuidanceConditions.Holds(problems[i].Resolution, _state)) Retire(i, context.Now);
                    else _trackers[i].Advance(context.DeltaSeconds, Activity);
                    break;
            }
        }

        var focus = FocusIndex();
        if (focus >= 0) TryEscalate(focus, context.Now);
    }

    /// <summary>Feeds a gameplay happening to every open problem's stuck score.</summary>
    public void Signal(in GuidanceSignal signal)
    {
        var problems = _tree.Problems;
        for (var i = 0; i < problems.Count; i++)
        {
            if (_status[i] != ProblemStatus.Open) continue;
            var tracker = _trackers[i];
            switch (signal.Kind)
            {
                case GuidanceSignalKind.FailedAction:
                    tracker.RecordFailedAction();
                    break;
                case GuidanceSignalKind.Diagnostic:
                    tracker.RecordDiagnostic(signal.Fact is not { } fact || problems[i].References(fact));
                    break;
                case GuidanceSignalKind.Repair:
                    tracker.RecordRepair();
                    break;
                case GuidanceSignalKind.Stabilization:
                    tracker.RecordStabilization();
                    break;
            }
        }
    }

    /// <summary>The player asked for a stronger hint: the next stage of the focused problem shows now, ignoring cooldown.</summary>
    /// <returns>False when no problem is open.</returns>
    public bool RequestStrongerHint(MissionTime now)
    {
        var focus = FocusIndex();
        if (focus < 0) return false;

        var next = _shown[focus] == HintStage.Direct ? HintStage.Direct : _shown[focus] + 1;
        _trackers[focus].RaiseTo((int)next * _settings.EscalationS);
        Emit(focus, next, now, requested: true);
        return true;
    }

    /// <summary>The player closed the card. The stage stays spent and the cooldown restarts.</summary>
    public bool Dismiss(MissionTime now)
    {
        if (CurrentTip is not { } tip) return false;
        CurrentTip = null;
        _currentProblem = -1;
        _lastTipMs = now.Milliseconds;
        _events.Publish(new TipWithdrawn(now, tip.ProblemId, TipWithdrawReason.Dismissed));
        return true;
    }

    public ProblemStatus StatusOf(string problemId) => _status[IndexOf(problemId)];

    public double StuckScoreS(string problemId) => _trackers[IndexOf(problemId)].ScoreS;

    public HintStage HighestStageShown(string problemId) => _shown[IndexOf(problemId)];

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < _status.Length; i++)
        {
            writer.Write((long)_status[i]);
            writer.Write((long)_shown[i]);
            _trackers[i].WriteHash(writer);
        }
        writer.Write(_lastTipMs.HasValue);
        writer.Write(_lastTipMs ?? 0);
        writer.Write(_currentProblem);
    }

    private void TryEscalate(int index, MissionTime now)
    {
        if (_shown[index] >= HintStage.Direct) return;
        var next = _shown[index] + 1;
        if (next > _settings.MaxUnsolicitedStage) return;
        if (next > _trackers[index].PermittedStage(_settings.EscalationS)) return;
        if (_lastTipMs is { } last && now.Milliseconds - last < _cooldownMs) return;
        Emit(index, next, now, requested: false);
    }

    private void Emit(int index, HintStage stage, MissionTime now, bool requested)
    {
        var problem = _tree.Problems[index];
        var content = problem.Stage(stage).Resolve(_state);
        var tip = new TipAvailable(now, problem.Id, stage, content.Text, content.FactRefs, content.ActionRefs, requested);

        _shown[index] = stage;
        _lastTipMs = now.Milliseconds;
        _currentProblem = index;
        CurrentTip = tip;
        _events.Publish(tip);
    }

    private void Retire(int index, MissionTime now)
    {
        _status[index] = ProblemStatus.Retired;
        if (_currentProblem != index) return;
        _currentProblem = -1;
        CurrentTip = null;
        _events.Publish(new TipWithdrawn(now, _tree.Problems[index].Id, TipWithdrawReason.Resolved));
    }

    private int FocusIndex()
    {
        var focus = -1;
        var problems = _tree.Problems;
        for (var i = 0; i < problems.Count; i++)
        {
            if (_status[i] != ProblemStatus.Open) continue;
            if (focus < 0 || problems[i].Priority > problems[focus].Priority) focus = i;
        }
        return focus;
    }

    private int IndexOf(string problemId)
    {
        var problems = _tree.Problems;
        for (var i = 0; i < problems.Count; i++)
        {
            if (string.Equals(problems[i].Id, problemId, StringComparison.Ordinal)) return i;
        }
        throw new ArgumentException($"Unknown problem '{problemId}'.", nameof(problemId));
    }
}
