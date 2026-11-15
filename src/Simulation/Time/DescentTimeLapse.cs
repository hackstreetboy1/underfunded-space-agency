namespace Usa.Simulation.Time;

/// <summary>
/// The fall under the chutes as one continuous time-lapse, from the drogue to splashdown in
/// <see cref="TimeCompressionConfig.DescentRealSecondsMax"/> real seconds. The rate holds a plateau, dips briefly to
/// <see cref="TimeCompressionConfig.DescentSlowRate"/> at each chute's deploy moment (easing in and out with
/// <see cref="TimeCompressionConfig.DescentEaseRealS"/>) and tapers along <c>slow + (plateau - slow)(1 - e^(-(τ/k)²))</c> over
/// the last real moments τ before touchdown (k = <see cref="TimeCompressionConfig.DescentTaperRealS"/>): an S-curve that leaves
/// the plateau about 2k before touchdown and is flat at the slow rate for the last frames, so it arrives without a step. The real time left τ is solved from the predicted MET to splashdown each frame, so a drifting
/// prediction bends the curve instead of cutting it; the plateau is re-planned towards the remaining budget until the taper.
/// </summary>
internal sealed class DescentTimeLapse
{
    private const int SolveIterations = 48;

    /// <summary>Time constant of the plateau's re-planning, real seconds.</summary>
    private const double ReplanRealS = 0.5;

    /// <summary>The plateau is no longer re-planned once the budget left is this many taper constants.</summary>
    private const double ReplanStopTapers = 3;

    private double _realLeftS;
    private double _plateauRate;

    public bool Active { get; private set; }

    /// <summary>Forgets the descent (the chutes are no longer out): the next one starts a fresh budget.</summary>
    public void Reset() => Active = false;

    /// <param name="splashdownMetS">Predicted MET to splashdown (positive).</param>
    /// <param name="deployMomentMetS">MET to or since the nearest chute deploy moment; infinity when none is near.</param>
    public double Rate(double splashdownMetS, double deployMomentMetS, double realDeltaS, double budgetRealS, TimeCompressionConfig config)
    {
        var slow = config.DescentSlowRate;
        var taper = config.DescentTaperRealS;
        if (!Active)
        {
            Active = true;
            _realLeftS = budgetRealS;
            _plateauRate = PlateauFor(splashdownMetS, budgetRealS, slow, taper);
        }
        else if (realDeltaS > 0)
        {
            _realLeftS = Math.Max(0, _realLeftS - realDeltaS);
            if (_realLeftS > ReplanStopTapers * taper)
            {
                var needed = PlateauFor(splashdownMetS, _realLeftS, slow, taper);
                _plateauRate += (needed - _plateauRate) * (1 - Math.Exp(-realDeltaS / ReplanRealS));
            }
        }

        var tauS = RealSecondsLeft(splashdownMetS, _plateauRate, slow, taper);
        var share = tauS / taper;
        var rate = slow + ((_plateauRate - slow) * (1 - Math.Exp(-share * share)));
        // Each deploy moment is approached and left exponentially: the rate is the MET gap over the ease constant.
        return Math.Max(slow, Math.Min(rate, Math.Max(slow, deployMomentMetS / config.DescentEaseRealS)));
    }

    /// <summary>MET the curve covers in its last <paramref name="tauS"/> real seconds.</summary>
    public static double MetCovered(double tauS, double plateauRate, double slowRate, double taperRealS) =>
        (slowRate * tauS) + ((plateauRate - slowRate) * TaperIntegral(tauS, taperRealS));

    /// <summary>The plateau rate that runs <paramref name="metS"/> in <paramref name="realS"/> along the curve.</summary>
    private static double PlateauFor(double metS, double realS, double slow, double taper)
    {
        var integral = TaperIntegral(realS, taper);
        return integral > 0 ? Math.Max(slow, slow + ((metS - (slow * realS)) / integral)) : slow;
    }

    /// <summary>∫₀^τ (1 - e^(-(s/k)²)) ds = τ - k (√π / 2) erf(τ / k).</summary>
    private static double TaperIntegral(double tauS, double taper) => tauS - (taper * HalfSqrtPi * Erf(tauS / taper));

    private const double HalfSqrtPi = 0.88622692545275801;

    /// <summary>erf(x) for x ≥ 0, Abramowitz and Stegun 7.1.26 (absolute error below 1.5e-7).</summary>
    private static double Erf(double x)
    {
        var t = 1 / (1 + (0.3275911 * x));
        var poly = t * (0.254829592 + (t * (-0.284496736 + (t * (1.421413741 + (t * (-1.453152027 + (t * 1.061405429))))))));
        return 1 - (poly * Math.Exp(-x * x));
    }

    /// <summary>The real seconds τ at which the curve has <paramref name="metS"/> MET left (bisection; the curve is monotonic).</summary>
    private static double RealSecondsLeft(double metS, double plateau, double slow, double taper)
    {
        double lo = 0, hi = metS / slow;
        for (var i = 0; i < SolveIterations; i++)
        {
            var mid = 0.5 * (lo + hi);
            if (MetCovered(mid, plateau, slow, taper) < metS) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
