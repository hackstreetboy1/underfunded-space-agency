using Usa.Simulation.Time;

namespace Usa.Simulation.Session.Scripts;

/// <summary>
/// Estimates the real time a played scenario takes under the host's automatic compression (<see cref="TimeCompressionDirector"/>):
/// each MET second costs 1 / rate real seconds. A second director models a player who presses "Advance to next event" whenever it
/// is offered (one real second per jump); a jump ends at its target or when the player acts.
/// </summary>
internal sealed class RealTimeEstimator(TimeCompressionConfig config)
{
    private const double JumpCostRealS = 1;

    private readonly TimeCompressionDirector _auto = new(config);
    private readonly TimeCompressionDirector _advance = new(config);
    private double _autoDeltaS = 1;
    private double _advanceDeltaS = 1;
    private long _skipUntilMs = -1;

    public double AutoRealS { get; private set; }

    /// <summary>Auto real seconds by the reason the director gave for its rate.</summary>
    public SortedDictionary<string, double> AutoRealSByReason { get; } = new(StringComparer.Ordinal);

    public double WithAdvanceRealS { get; private set; }

    /// <summary>Accounts for the MET second that just ran.</summary>
    /// <param name="acted">The player submitted something at the start of that second.</param>
    public void Step(in CompressionInputs inputs, bool acted)
    {
        _auto.Update(inputs, _autoDeltaS);
        _autoDeltaS = 1 / _auto.TargetRate;
        AutoRealS += _autoDeltaS;
        var reason = _auto.Reason.ToString();
        AutoRealSByReason[reason] = AutoRealSByReason.GetValueOrDefault(reason) + _autoDeltaS;

        if (acted) _skipUntilMs = -1;
        if (_skipUntilMs >= 0 && inputs.Now.Milliseconds < _skipUntilMs) return;
        _skipUntilMs = -1;
        _advance.Update(inputs, _advanceDeltaS);
        if (_advance.CanAdvanceToNextEvent && _advance.NextEventAdvanceTarget is { } target)
        {
            _skipUntilMs = target.Milliseconds;
            WithAdvanceRealS += JumpCostRealS;
            _advanceDeltaS = JumpCostRealS;
            return;
        }
        _advanceDeltaS = 1 / _advance.TargetRate;
        WithAdvanceRealS += _advanceDeltaS;
    }
}
