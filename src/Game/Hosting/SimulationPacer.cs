using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;
using Usa.Simulation.Time;

namespace Usa.Game.Hosting;

/// <summary>
/// Turns real frame time into simulation ticks (SCRIPTING 12.2): the MET target grows by real delta × the
/// <see cref="TimeCompressionDirector"/> rate, ticks run in chunks of <see cref="PacingSettings.TicksPerBudgetCheck"/>
/// until the target or the frame budget is reached, and any backlog beyond <see cref="PacingSettings.MaxBacklogMs"/>
/// is dropped so an overloaded frame lowers the effective rate instead of spiralling.
/// </summary>
/// <remarks>
/// Between chunks the compression inputs are re-read, so a critical alarm or an approaching event that lowers the
/// rate mid-frame scales the rest of the frame down immediately. During the recovery wait the director's rate is far above
/// the ladder and the frame budget is <see cref="PacingSettings.RecoveryWaitFrameBudget"/>. While frozen nothing runs and the target follows
/// MET, so unfreezing never produces a catch-up burst. Pacing is presentation-only: results are identical at any
/// rate because the tick size never changes. While the director fast-forwards a wait the frame budget is also
/// <see cref="PacingSettings.RecoveryWaitFrameBudget"/>, and a held burn order's mark (<see cref="TimeCompressionDirector.StopAt"/>)
/// is never overrun.
/// </remarks>
public sealed class SimulationPacer
{
    private readonly ISimulationSessionPort _session;
    private readonly TimeCompressionDirector _director;
    private readonly IMonotonicClock _clock;
    private readonly PacingSettings _settings;
    private double _metTargetMs;
    private MissionTime? _advanceTarget;
    private double _lastRate = 1;

    public SimulationPacer(ISimulationSessionPort session, TimeCompressionDirector director, IMonotonicClock clock, PacingSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(director);
        ArgumentNullException.ThrowIfNull(clock);
        _settings = settings ?? PacingSettings.Default;
        _settings.Validate();
        _session = session;
        _director = director;
        _clock = clock;
        _metTargetMs = session.Now.Milliseconds;
    }

    /// <summary>
    /// The rate the director asks for (smoothed), or the advance rate while advancing to the next event; <see cref="EffectiveRate"/>
    /// is what was achieved.
    /// </summary>
    public double CompressionRate => _advanceTarget is not null && double.IsFinite(_director.AdvanceRateMax)
        ? Math.Max(_director.TargetRate, _director.AdvanceRateMax)
        : _director.TargetRate;

    public CompressionReason CompressionReason => _director.Reason;

    /// <summary>MET advanced per real second, averaged; drops below <see cref="CompressionRate"/> when over budget.</summary>
    public double EffectiveRate { get; private set; } = 1;

    /// <summary>Enables the "Advance to next event" button.</summary>
    public bool CanAdvanceToNextEvent => _advanceTarget is null && !_session.IsFrozen && _director.CanAdvanceToNextEvent;

    public bool IsAdvancingToEvent => _advanceTarget is not null;

    /// <summary>Where the current advance-to-event stops (event time minus the lead).</summary>
    public MissionTime? AdvanceTarget => _advanceTarget;

    /// <summary>MET the host still owes the session.</summary>
    public double BacklogMs => Math.Max(0, _metTargetMs - _session.Now.Milliseconds);

    /// <summary>
    /// Skips ahead to the next scheduled event minus the lead time (or over a quiet coast) at the director's
    /// <see cref="TimeCompressionDirector.AdvanceRateMax"/> (250x as shipped), with the fast-forward frame budget. Stops early when
    /// anything needs the player (alarm, warning, confirmation, a guide step waiting for them, a decisive-event window) or the
    /// session freezes.
    /// </summary>
    /// <returns>False when advancing is not allowed now.</returns>
    public bool RequestAdvanceToNextEvent()
    {
        // A target less than one tick ahead can never be reached: the advance would run no ticks, end, and (re-requested)
        // hold the clock still while also resetting the normal pacing target.
        if (!CanAdvanceToNextEvent || _director.NextEventAdvanceTarget is not { } target
            || target.Milliseconds - _session.Now.Milliseconds < SimulationClock.BaseTickMs) return false;
        _advanceTarget = target;
        return true;
    }

    public void CancelAdvanceToNextEvent()
    {
        _advanceTarget = null;
        _metTargetMs = _session.Now.Milliseconds;
    }

    public PacerFrame Step(double realDeltaS)
    {
        if (!(realDeltaS >= 0) || double.IsInfinity(realDeltaS))
            throw new ArgumentOutOfRangeException(nameof(realDeltaS), realDeltaS, "Real delta must be a finite, non-negative number of seconds.");

        var before = _session.Now;
        if (_session.IsFrozen)
        {
            _advanceTarget = null;
            _metTargetMs = before.Milliseconds;
            EffectiveRate = 0;
            return new PacerFrame(0, before, before, BudgetExhausted: false, DroppedMs: 0, Frozen: true);
        }

        _director.Update(_session.ReadCompressionInputs(), realDeltaS);
        var rate = _director.TargetRate;
        double targetMs;
        if (RefreshAdvanceTarget() is { } advanceTarget)
        {
            var advanceRate = Math.Max(rate, _director.AdvanceRateMax);
            rate = double.IsFinite(advanceRate) ? advanceRate : rate;
            targetMs = double.IsFinite(advanceRate)
                ? Math.Min(advanceTarget.Milliseconds, before.Milliseconds + (realDeltaS * 1000.0 * advanceRate))
                : advanceTarget.Milliseconds;
            _metTargetMs = targetMs;
        }
        else
        {
            _metTargetMs = Math.Max(_metTargetMs, before.Milliseconds);
            if (rate < _lastRate) _metTargetMs = ScaleRemaining(before.Milliseconds, _metTargetMs, rate / _lastRate);
            _metTargetMs += realDeltaS * 1000.0 * rate;
            _metTargetMs = ClampToStop(_metTargetMs);
            targetMs = _metTargetMs;
        }

        (var ticks, var stoppedByBudget, _lastRate) = RunWithinBudget(ref targetMs, rate);

        var after = _session.Now;
        double droppedMs = 0;
        if (_advanceTarget is { } target)
        {
            if (target.Milliseconds - after.Milliseconds < SimulationClock.BaseTickMs) _advanceTarget = null;
            _metTargetMs = after.Milliseconds;
        }
        else
        {
            var limitMs = after.Milliseconds + (double)_settings.MaxBacklogMs;
            if (_metTargetMs > limitMs)
            {
                droppedMs = _metTargetMs - limitMs;
                _metTargetMs = limitMs;
            }
        }

        UpdateEffectiveRate(after.Milliseconds - before.Milliseconds, realDeltaS);
        var behind = targetMs - after.Milliseconds >= SimulationClock.BaseTickMs;
        return new PacerFrame(ticks, before, after, stoppedByBudget && behind, droppedMs, Frozen: false);
    }

    /// <returns>Ticks run, whether the budget stopped the loop, and the rate in force at the end.</returns>
    private (int Ticks, bool StoppedByBudget, double FinalRate) RunWithinBudget(ref double targetMs, double rate)
    {
        var chunk = _settings.TicksPerBudgetCheck;
        var ticks = 0;
        var start = _clock.Elapsed;
        while (true)
        {
            var ran = _session.Advance(new MissionTime((long)targetMs), chunk);
            ticks += ran;
            if (ran < chunk) return (ticks, false, rate);
            var budget = _director.IsRecoveryWait || _director.IsFastForward || _advanceTarget is not null
                ? _settings.RecoveryWaitFrameBudget
                : _settings.FrameBudget;
            if (_clock.Elapsed - start >= budget) return (ticks, true, rate);

            _director.Update(_session.ReadCompressionInputs(), 0);
            if (_advanceTarget is not null)
            {
                if (RefreshAdvanceTarget() is { } refreshed)
                {
                    targetMs = Math.Min(targetMs, refreshed.Milliseconds);
                    _metTargetMs = targetMs;
                    continue;
                }
                targetMs = _metTargetMs;
                return (ticks, false, _director.TargetRate);
            }

            var newRate = _director.TargetRate;
            if (newRate < rate)
            {
                targetMs = ScaleRemaining(_session.Now.Milliseconds, targetMs, newRate / rate);
                _metTargetMs = targetMs;
                rate = newRate;
            }
            if (_director.StopAt is not null)
            {
                targetMs = ClampToStop(targetMs);
                _metTargetMs = Math.Min(_metTargetMs, targetMs);
            }
        }
    }

    /// <summary>A held burn order is given at its mark: never run past it, whatever the rate (pacing must not move the order).</summary>
    private double ClampToStop(double targetMs) =>
        _director.StopAt is { } stop && targetMs > stop.Milliseconds ? Math.Max(stop.Milliseconds, _session.Now.Milliseconds) : targetMs;

    // Owed MET shrinks with the rate, as if it had been accrued at the new rate; otherwise a rate drop
    // (critical alarm, approaching event) would still run up to a full backlog at the old rate.
    private static double ScaleRemaining(double nowMs, double targetMs, double factor) =>
        targetMs <= nowMs ? targetMs : nowMs + ((targetMs - nowMs) * factor);

    /// <summary>Follows the director's latest event target; cancels the advance when it no longer allows one.</summary>
    private MissionTime? RefreshAdvanceTarget()
    {
        if (_advanceTarget is not { } requested) return null;
        if (!_director.CanAdvanceToNextEvent || _director.NextEventAdvanceTarget is not { } latest)
        {
            _advanceTarget = null;
            _metTargetMs = _session.Now.Milliseconds;
            return null;
        }
        _advanceTarget = latest < requested ? latest : requested;
        if (_director.StopAt is { } stop && stop < _advanceTarget && stop > _session.Now) _advanceTarget = stop;
        return _advanceTarget;
    }

    private void UpdateEffectiveRate(long advancedMs, double realDeltaS)
    {
        if (realDeltaS <= 0) return;
        var instantRate = advancedMs / (realDeltaS * 1000.0);
        var tau = _settings.EffectiveRateSmoothingRealS;
        var alpha = tau <= 0 ? 1 : 1 - Math.Exp(-realDeltaS / tau);
        EffectiveRate += alpha * (instantRate - EffectiveRate);
    }
}
