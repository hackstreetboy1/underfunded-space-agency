using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Simulation.Knowledge.Ledger;

/// <summary>
/// The ground's mass ledger for one vehicle (X8): design dry masses (±<see cref="GroundMassLedgerSpec.DryTolerance"/>),
/// known jettisons and draws, and each tank's propellant knowledge. It reads only the knowledge base and vehicle data,
/// never the simulated mass, so its band is what the planner, previews and ground tracking may use.
/// </summary>
/// <remarks>
/// Per tank, in order of preference: a fresh gauge reading ± tolerance (conflicting fresh readings give their union);
/// a stale reading newer than the ground band; the current ground band word; else 0 to capacity. Draws made after a
/// reading are subtracted from it, and every band is capped at capacity less all known draws. A confirmed gauge bias
/// (<c>bias_high</c>/<c>bias_low</c> variant) is corrected and stated; a suspected one widens the band by the authored
/// magnitude; one the ground has not diagnosed passes through uncorrected.
/// </remarks>
public sealed class GroundMassLedger
{
    public const string BiasHighVariant = "bias_high";
    public const string BiasLowVariant = "bias_low";

    private readonly DownlinkDefinition?[] _downlinks;

    /// <exception cref="ArgumentException">The spec is inconsistent or names a fact that is not a number fact in <paramref name="catalog"/>.</exception>
    public GroundMassLedger(GroundMassLedgerSpec spec, FactCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(catalog);
        Validate(spec);
        Spec = spec;
        _downlinks = new DownlinkDefinition?[spec.Tanks.Count];
        for (var i = 0; i < spec.Tanks.Count; i++)
        {
            var tank = spec.Tanks[i];
            if (!catalog.TryResolve(tank.PropellantFact, out var fact) || fact.Kind != FactValueKind.Number)
                throw new ArgumentException($"Tank '{tank.Id}' needs number fact '{tank.PropellantFact}' in the catalog.", nameof(spec));
            _downlinks[i] = fact.Downlink;
        }
    }

    public GroundMassLedgerSpec Spec { get; }

    /// <summary>The ledger's mass band as of <see cref="KnowledgeBase.Now"/>. Deterministic for equal knowledge and events.</summary>
    public GroundMassEstimate Estimate(KnowledgeBase knowledge, KnownMassEvents events)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(events);
        var now = knowledge.Now;

        var dryKg = 0.0;
        foreach (var item in Spec.DryMasses)
        {
            if (!events.Removed.Contains(item.Id) && !events.Removed.Contains(item.Section)) dryKg += item.DesignMassKg;
        }
        var tolerance = Spec.DryTolerance;
        var dry = new MassBand(dryKg * (1 - tolerance), dryKg, dryKg * (1 + tolerance), LedgerSources.GroundLedger, now, KnowledgeState.Estimated);

        var tanks = new TankEstimate[Spec.Tanks.Count];
        double low = dry.LowKg, best = dry.BestKg, high = dry.HighKg;
        for (var i = 0; i < tanks.Length; i++)
        {
            var estimate = EstimateTank(i, knowledge, events);
            tanks[i] = estimate;
            if (!estimate.Attached) continue;
            low += estimate.Propellant.LowKg;
            best += estimate.Propellant.BestKg;
            high += estimate.Propellant.HighKg;
        }

        return new GroundMassEstimate(new MassBand(low, best, high, LedgerSources.GroundLedger, now, KnowledgeState.Estimated), dry, tanks);
    }

    private TankEstimate EstimateTank(int index, KnowledgeBase knowledge, KnownMassEvents events)
    {
        var tank = Spec.Tanks[index];
        if (events.Removed.Contains(tank.Id) || events.Removed.Contains(tank.Section))
            return new TankEstimate(tank.Id, new MassBand(0, 0, 0, LedgerSources.GroundLedger, null, KnowledgeState.Known), TankKnowledgeBasis.Unknown, GaugeBiasTreatment.None, false);

        var (treatment, signedBiasKg) = BiasOf(tank, knowledge);
        var ceilingKg = Math.Max(0, tank.CapacityKg - DrawnKg(tank.Id, events, after: null));
        var fact = knowledge.Describe(tank.PropellantFact);
        var reading = treatment == GaugeBiasTreatment.GaugeFailed ? null : fact.Latest;
        var fresh = fact.State is KnowledgeState.Known or KnowledgeState.Conflicting;
        var groundBand = fact.GroundBand;

        if (reading?.Value is not null && (fresh || groundBand is null || reading.Time >= groundBand.Time))
        {
            var (low, best, high) = ReadingBand(reading, tank, treatment, signedBiasKg, events);
            if (treatment != GaugeBiasTreatment.GaugeFailed)
            {
                foreach (var other in fact.ConflictsWith)
                {
                    if (other.Value is null) continue;
                    var (otherLow, _, otherHigh) = ReadingBand(other, tank, treatment, signedBiasKg, events);
                    low = Math.Min(low, otherLow);
                    high = Math.Max(high, otherHigh);
                }
            }
            var state = fresh ? fact.State : KnowledgeState.Stale;
            var source = treatment switch
            {
                GaugeBiasTreatment.Corrected => LedgerSources.GaugeReading + ", " + LedgerSources.CorrectedForGaugeBias,
                GaugeBiasTreatment.Widened => LedgerSources.GaugeReading + ", " + LedgerSources.SuspectedGaugeBias,
                _ => LedgerSources.GaugeReading,
            };
            return new TankEstimate(tank.Id, Clamp(low, best, high, ceilingKg, source, reading.Time, state), TankKnowledgeBasis.Reading, treatment, true);
        }

        // The current band word holds until it changes, so draws need no subtraction beyond the capacity cap.
        if (groundBand?.Qualitative is { } word && BandLimits(_downlinks[index], word, tank.CapacityKg) is { } limits)
        {
            return new TankEstimate(
                tank.Id,
                Clamp(limits.Low, (limits.Low + limits.High) / 2, limits.High, ceilingKg, LedgerSources.GroundBand + " " + word, groundBand.Time, KnowledgeState.Estimated),
                TankKnowledgeBasis.GroundBand,
                treatment,
                true);
        }

        var unknownSource = treatment == GaugeBiasTreatment.GaugeFailed ? LedgerSources.Unknown + ", " + LedgerSources.GaugeFailed : LedgerSources.Unknown;
        return new TankEstimate(
            tank.Id,
            new MassBand(0, ceilingKg / 2, ceilingKg, unknownSource, null, KnowledgeState.Unknown),
            TankKnowledgeBasis.Unknown,
            treatment,
            true);
    }

    private static (double Low, double Best, double High) ReadingBand(
        Observation reading, LedgerTank tank, GaugeBiasTreatment treatment, double signedBiasKg, KnownMassEvents events)
    {
        var value = reading.Value!.Value;
        var widenKg = 0.0;
        // The bias belongs to this tank's gauge; a reading of another fact in its conflict group is another instrument.
        if (reading.Fact == tank.PropellantFact)
        {
            if (treatment == GaugeBiasTreatment.Corrected) value -= signedBiasKg;
            else if (treatment == GaugeBiasTreatment.Widened) widenKg = tank.GaugeBiasKg;
        }
        var drawnKg = DrawnKg(tank.Id, events, after: reading.Time);
        var best = value - drawnKg;
        return (best - tank.ReadingToleranceKg - widenKg, best, best + tank.ReadingToleranceKg + widenKg);
    }

    private static (GaugeBiasTreatment Treatment, double SignedBiasKg) BiasOf(LedgerTank tank, KnowledgeBase knowledge)
    {
        if (tank.Gauge is not { } gauge) return (GaugeBiasTreatment.None, 0);
        var component = knowledge.ComponentOf(gauge);
        switch (component.State)
        {
            case ComponentKnowledgeState.ConfirmedDamaged when tank.GaugeBiasKg > 0 && component.Variant == BiasHighVariant:
                return (GaugeBiasTreatment.Corrected, tank.GaugeBiasKg);
            case ComponentKnowledgeState.ConfirmedDamaged when tank.GaugeBiasKg > 0 && component.Variant == BiasLowVariant:
                return (GaugeBiasTreatment.Corrected, -tank.GaugeBiasKg);
            case ComponentKnowledgeState.ConfirmedDamaged:
                return (GaugeBiasTreatment.GaugeFailed, 0);
            case ComponentKnowledgeState.Suspected when tank.GaugeBiasKg > 0:
                return (GaugeBiasTreatment.Widened, 0);
            default:
                return (GaugeBiasTreatment.None, 0);
        }
    }

    /// <summary>Lower and upper limit of a band word: from its own lower bound (0 for the first) to the next band's.</summary>
    private static (double Low, double High)? BandLimits(DownlinkDefinition? downlink, string word, double capacityKg)
    {
        if (downlink is null) return null;
        var bands = downlink.Bands;
        for (var i = 0; i < bands.Count; i++)
        {
            if (!string.Equals(bands[i].Word, word, StringComparison.Ordinal)) continue;
            var low = bands[i].From ?? 0;
            var high = i + 1 < bands.Count && bands[i + 1].From is { } next ? next : capacityKg;
            return (low, high);
        }
        return null;
    }

    private static double DrawnKg(string tankId, KnownMassEvents events, MissionTime? after)
    {
        var total = 0.0;
        foreach (var draw in events.Draws)
        {
            if (string.Equals(draw.TankId, tankId, StringComparison.Ordinal) && (after is not { } time || draw.Time > time)) total += draw.MassKg;
        }
        return total;
    }

    private static MassBand Clamp(double low, double best, double high, double ceilingKg, string source, MissionTime met, KnowledgeState state)
    {
        high = Math.Clamp(high, 0, ceilingKg);
        low = Math.Clamp(low, 0, high);
        return new MassBand(low, Math.Clamp(best, low, high), high, source, met, state);
    }

    private static void Validate(GroundMassLedgerSpec spec)
    {
        if (!(spec.DryTolerance is >= 0 and < 1)) throw new ArgumentException("Dry tolerance must be in [0, 1).", nameof(spec));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in spec.DryMasses)
        {
            if (!ids.Add(item.Id)) throw new ArgumentException($"Duplicate ledger id '{item.Id}'.", nameof(spec));
            if (!(item.DesignMassKg >= 0) || double.IsInfinity(item.DesignMassKg))
                throw new ArgumentException($"Dry mass '{item.Id}' needs a finite, non-negative mass.", nameof(spec));
        }
        foreach (var tank in spec.Tanks)
        {
            if (!ids.Add(tank.Id)) throw new ArgumentException($"Duplicate ledger id '{tank.Id}'.", nameof(spec));
            if (!(tank.CapacityKg > 0) || double.IsInfinity(tank.CapacityKg))
                throw new ArgumentException($"Tank '{tank.Id}' needs a positive capacity.", nameof(spec));
            if (!(tank.ReadingToleranceKg >= 0) || !(tank.GaugeBiasKg >= 0))
                throw new ArgumentException($"Tank '{tank.Id}' needs non-negative reading tolerance and gauge bias.", nameof(spec));
        }
    }
}
