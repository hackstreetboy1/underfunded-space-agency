using Usa.Simulation.Kernel;

namespace Usa.Simulation.Forecast;

/// <summary>
/// Projects every resource along the plan twice: pessimistically (less in stock, less produced, more consumed, by
/// the uncertainty of each input's knowledge) and optimistically. The earliest pessimistic depletion names the
/// limiting factor; the earliest depletion of each case bounds the safe autonomy range. Stale or unknown inputs
/// widen the range and lower confidence.
/// </summary>
/// <remarks>
/// Runs every 30 s of MET inside a tick, so it allocates only its result: the id and time orders are index spans on
/// the stack, and no input list is enumerated through an interface.
/// </remarks>
public sealed class SurvivalForecast(ForecastConfig config)
{
    private const double SecondsPerHour = 3600.0;

    /// <summary>Orders of up to this many entries live on the stack; longer ones (not seen in play) take an array.</summary>
    private const int StackOrderLimit = 64;

    private readonly ForecastConfig _config = config ?? throw new ArgumentNullException(nameof(config));

    public ForecastConfig Config => _config;

    public ForecastResult Compute(ForecastInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var resourceCount = input.Resources.Count;
        Span<int> resourceOrder = resourceCount <= StackOrderLimit ? stackalloc int[resourceCount] : new int[resourceCount];
        var resources = OrderedResources.ById(input.Resources, resourceOrder);
        for (var i = 1; i < resources.Length; i++)
        {
            if (string.Equals(resources[i].Id, resources[i - 1].Id, StringComparison.Ordinal))
                throw new ArgumentException($"Resource '{resources[i].Id}' is listed twice.", nameof(input));
        }
        // A segment already in the past has been executed; its effect is in the current inputs, so re-applying it
        // (e.g. a SetQuantity) would undo what happened since.
        var segmentCount = input.Plan.Count;
        Span<int> planOrder = segmentCount <= StackOrderLimit ? stackalloc int[segmentCount] : new int[segmentCount];
        var plan = PendingPlan.From(input.Plan, input.Now, planOrder);
        CheckPlanReferences(plan, resources);

        var horizonH = _config.HorizonH;
        var forecasts = new ResourceForecast[resources.Length];
        for (var i = 0; i < resources.Length; i++)
        {
            var resource = resources[i];
            var uq = _config.UncertaintyOf(resource.QuantityKnowledge);
            var up = _config.UncertaintyOf(resource.ProductionKnowledge);
            var uc = _config.UncertaintyOf(resource.ConsumptionKnowledge);
            var lowH = HoursToReserve(resource, input, plan, 1 - uq, 1 - up, 1 + uc, horizonH);
            var highH = HoursToReserve(resource, input, plan, 1 + uq, 1 + up, Math.Max(0, 1 - uc), horizonH);
            forecasts[i] = new ResourceForecast(resource.Id, lowH, Math.Max(lowH, highH), WorstKnowledge(resource, plan));
        }

        var limiting = -1;
        var rawLowH = double.PositiveInfinity;
        var rawHighH = double.PositiveInfinity;
        for (var i = 0; i < forecasts.Length; i++)
        {
            if (forecasts[i].LowH < rawLowH)
            {
                rawLowH = forecasts[i].LowH;
                limiting = i;
            }
            rawHighH = Math.Min(rawHighH, forecasts[i].HighH);
        }

        var range = DisplayRange(rawLowH, rawHighH, horizonH);
        var confidence = Confidence(forecasts, rawLowH, rawHighH);
        var coverage = Coverage(input, rawLowH, rawHighH);
        var beyondHorizon = double.IsPositiveInfinity(rawLowH);
        var assumptions = new ForecastAssumption[WriteAssumptions(null, input, resources, plan, beyondHorizon)];
        WriteAssumptions(assumptions, input, resources, plan, beyondHorizon);
        return new ForecastResult(
            input.Now, range, limiting >= 0 ? forecasts[limiting].Id : "", confidence, coverage, forecasts, assumptions);
    }

    private static double HoursToReserve(
        ForecastResource resource, ForecastInput input, PendingPlan plan,
        double quantityFactor, double productionFactor, double consumptionFactor, double horizonH)
    {
        var quantity = resource.Quantity * Math.Max(0, quantityFactor);
        var production = resource.ProductionPerH;
        var consumption = resource.ConsumptionPerH;
        var crew = input.CrewCount;
        var rationFraction = resource.Ration switch
        {
            RationKind.Food => input.FoodRationPct / 100.0,
            RationKind.Water => input.WaterRationPct / 100.0,
            _ => 1.0,
        };
        var perCrew = resource.PerCrewConsumptionPerH * rationFraction + resource.PerCrewOverheadPerH;
        var reserve = resource.ReserveQuantity;

        var t = 0.0;
        var next = 0;
        while (true)
        {
            while (next < plan.Length && HoursFrom(input.Now, plan[next].Start) <= t)
            {
                var segment = plan[next++];
                if (segment.CrewCount is { } count) crew = count;
                var changes = segment.Changes;
                for (var c = 0; c < changes.Count; c++)
                {
                    var change = changes[c];
                    if (!string.Equals(change.ResourceId, resource.Id, StringComparison.Ordinal)) continue;
                    switch (change.Kind)
                    {
                        case PlanChangeKind.SetQuantity: quantity = change.Value * Math.Max(0, quantityFactor); break;
                        case PlanChangeKind.AddQuantity: quantity += change.Value * Math.Max(0, quantityFactor); break;
                        case PlanChangeKind.SetProductionPerH: production = change.Value; break;
                        case PlanChangeKind.ScaleProduction: production *= change.Value; break;
                        case PlanChangeKind.SetConsumptionPerH: consumption = change.Value; break;
                        case PlanChangeKind.AddConsumptionPerH: consumption += change.Value; break;
                        case PlanChangeKind.ScaleConsumption: consumption *= change.Value; break;
                    }
                }
            }

            if (quantity <= reserve) return t;
            var segmentEnd = next < plan.Length ? Math.Min(HoursFrom(input.Now, plan[next].Start), horizonH) : horizonH;
            var ratePerH = production * Math.Max(0, productionFactor) - (consumption + crew * perCrew) * consumptionFactor;
            if (ratePerH < 0)
            {
                var hit = t + (quantity - reserve) / -ratePerH;
                if (hit <= segmentEnd) return hit;
            }
            quantity += ratePerH * (segmentEnd - t);
            t = segmentEnd;
            if (t >= horizonH) return double.PositiveInfinity;
        }
    }

    private AutonomyRange DisplayRange(double rawLowH, double rawHighH, double horizonH)
    {
        if (double.IsPositiveInfinity(rawLowH)) return new AutonomyRange(horizonH, double.PositiveInfinity);

        var step = _config.RoundingH;
        var low = Math.Floor(rawLowH / step) * step;
        if (double.IsPositiveInfinity(rawHighH)) return new AutonomyRange(low, double.PositiveInfinity);

        var high = Math.Max(rawHighH, rawLowH * (1 + _config.MinBandFraction));
        high = Math.Ceiling(high / step) * step;
        if (high <= low) high = low + step;
        return new AutonomyRange(low, high);
    }

    private ForecastConfidence Confidence(ResourceForecast[] forecasts, double rawLowH, double rawHighH)
    {
        var worst = InputKnowledge.Fresh;
        foreach (var forecast in forecasts)
        {
            // Only resources that could be the first to run out bear on how sure the range is.
            if (double.IsPositiveInfinity(forecast.LowH) || forecast.LowH > rawHighH) continue;
            if (forecast.WorstKnowledge > worst) worst = forecast.WorstKnowledge;
        }
        var byKnowledge = worst switch
        {
            InputKnowledge.Fresh => ForecastConfidence.High,
            InputKnowledge.Stale => ForecastConfidence.Medium,
            _ => ForecastConfidence.Low,
        };

        if (double.IsPositiveInfinity(rawLowH)) return byKnowledge;
        var relativeWidth = double.IsPositiveInfinity(rawHighH)
            ? double.PositiveInfinity
            : (rawHighH - rawLowH) / Math.Max(rawLowH, _config.RoundingH);
        var byWidth = relativeWidth <= _config.Confidence.HighMaxRelativeWidth ? ForecastConfidence.High
            : relativeWidth <= _config.Confidence.MediumMaxRelativeWidth ? ForecastConfidence.Medium
            : ForecastConfidence.Low;
        return byKnowledge < byWidth ? byKnowledge : byWidth;
    }

    private static PlanCoverage Coverage(ForecastInput input, double rawLowH, double rawHighH)
    {
        if (input.PlanEnd is not { } end) return PlanCoverage.NoPlanEnd;
        var requiredH = HoursFrom(input.Now, end);
        if (rawLowH >= requiredH) return PlanCoverage.Covered;
        return rawHighH >= requiredH ? PlanCoverage.AtRisk : PlanCoverage.NotCovered;
    }

    /// <summary>Writes the assumptions in display order into <paramref name="into"/>, or only counts them when it is null.</summary>
    private int WriteAssumptions(
        ForecastAssumption[]? into, ForecastInput input, OrderedResources resources, PendingPlan plan, bool beyondHorizon)
    {
        var count = 0;
        Add(into, ref count, ForecastAssumptionKeys.CrewCount, "", input.CrewCount);
        if (input.FoodRationPct < 100) Add(into, ref count, ForecastAssumptionKeys.FoodRation, "", input.FoodRationPct);
        if (input.WaterRationPct < 100) Add(into, ref count, ForecastAssumptionKeys.WaterRation, "", input.WaterRationPct);

        for (var i = 0; i < resources.Length; i++)
        {
            var resource = resources[i];
            AddKnowledge(into, ref count, resource.Id, resource.QuantityKnowledge, ForecastAssumptionKeys.QuantityStale, ForecastAssumptionKeys.QuantityUnknown);
            if (HasProduction(resource, plan))
                AddKnowledge(into, ref count, resource.Id, resource.ProductionKnowledge, ForecastAssumptionKeys.ProductionStale, ForecastAssumptionKeys.ProductionUnknown);
            if (HasConsumption(resource, plan))
                AddKnowledge(into, ref count, resource.Id, resource.ConsumptionKnowledge, ForecastAssumptionKeys.ConsumptionStale, ForecastAssumptionKeys.ConsumptionUnknown);
        }

        for (var i = 0; i < plan.Length; i++)
        {
            var segment = plan[i];
            var startH = HoursFrom(input.Now, segment.Start);
            if (startH <= _config.HorizonH) Add(into, ref count, ForecastAssumptionKeys.PlanSegment, segment.Id, Math.Max(0, startH));
        }
        if (input.PlanEnd is { } end) Add(into, ref count, ForecastAssumptionKeys.PlanEnd, "", HoursFrom(input.Now, end));
        if (beyondHorizon) Add(into, ref count, ForecastAssumptionKeys.BeyondHorizon, "", _config.HorizonH);
        return count;
    }

    private static void AddKnowledge(
        ForecastAssumption[]? into, ref int count, string id, InputKnowledge knowledge, string staleKey, string unknownKey)
    {
        if (knowledge == InputKnowledge.Stale) Add(into, ref count, staleKey, id, 0);
        else if (knowledge == InputKnowledge.Unknown) Add(into, ref count, unknownKey, id, 0);
    }

    private static void Add(ForecastAssumption[]? into, ref int count, string key, string subject, double value)
    {
        if (into is not null) into[count] = new ForecastAssumption(key, subject, value);
        count++;
    }

    private static InputKnowledge WorstKnowledge(ForecastResource resource, PendingPlan plan)
    {
        var worst = resource.QuantityKnowledge;
        if (HasProduction(resource, plan) && resource.ProductionKnowledge > worst) worst = resource.ProductionKnowledge;
        if (HasConsumption(resource, plan) && resource.ConsumptionKnowledge > worst) worst = resource.ConsumptionKnowledge;
        return worst;
    }

    private static bool HasProduction(ForecastResource resource, PendingPlan plan) =>
        resource.ProductionPerH != 0 || Touches(resource.Id, plan, PlanChangeKind.SetProductionPerH, PlanChangeKind.ScaleProduction);

    private static bool HasConsumption(ForecastResource resource, PendingPlan plan) =>
        resource.ConsumptionPerH != 0 || resource.PerCrewConsumptionPerH != 0 || resource.PerCrewOverheadPerH != 0
        || Touches(resource.Id, plan, PlanChangeKind.SetConsumptionPerH, PlanChangeKind.AddConsumptionPerH);

    private static bool Touches(string id, PendingPlan plan, PlanChangeKind first, PlanChangeKind second)
    {
        for (var i = 0; i < plan.Length; i++)
        {
            var changes = plan[i].Changes;
            for (var c = 0; c < changes.Count; c++)
            {
                var change = changes[c];
                if ((change.Kind == first || change.Kind == second) && string.Equals(change.ResourceId, id, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static void CheckPlanReferences(PendingPlan plan, OrderedResources resources)
    {
        for (var i = 0; i < plan.Length; i++)
        {
            var segment = plan[i];
            var changes = segment.Changes;
            for (var c = 0; c < changes.Count; c++)
            {
                var known = false;
                for (var r = 0; r < resources.Length; r++)
                {
                    if (string.Equals(resources[r].Id, changes[c].ResourceId, StringComparison.Ordinal)) known = true;
                }
                if (!known)
                    throw new ArgumentException($"Plan segment '{segment.Id}' changes unknown resource '{changes[c].ResourceId}'.", nameof(plan));
            }
        }
    }

    private static double HoursFrom(MissionTime now, MissionTime then) => (then.Milliseconds - now.Milliseconds) / 1000.0 / SecondsPerHour;

    /// <summary>The input's resources seen through an index order (ordinal by id), without copying them.</summary>
    private readonly ref struct OrderedResources(IReadOnlyList<ForecastResource> resources, ReadOnlySpan<int> order)
    {
        private readonly IReadOnlyList<ForecastResource> _resources = resources;
        private readonly ReadOnlySpan<int> _order = order;

        public int Length => _order.Length;

        public ForecastResource this[int index] => _resources[_order[index]];

        /// <summary>Fills <paramref name="order"/> (one slot per resource) by insertion sort; inputs hold a handful of resources.</summary>
        public static OrderedResources ById(IReadOnlyList<ForecastResource> resources, Span<int> order)
        {
            for (var i = 0; i < order.Length; i++)
            {
                var id = resources[i].Id;
                var at = i;
                while (at > 0 && string.CompareOrdinal(resources[order[at - 1]].Id, id) > 0)
                {
                    order[at] = order[at - 1];
                    at--;
                }
                order[at] = i;
            }
            return new OrderedResources(resources, order);
        }
    }

    /// <summary>The plan's segments from now on, by start then id (stable, like the LINQ order it replaced).</summary>
    private readonly ref struct PendingPlan(IReadOnlyList<PlanSegment> segments, ReadOnlySpan<int> order)
    {
        private readonly IReadOnlyList<PlanSegment> _segments = segments;
        private readonly ReadOnlySpan<int> _order = order;

        public int Length => _order.Length;

        public PlanSegment this[int index] => _segments[_order[index]];

        /// <param name="order">At least one slot per segment; the pending ones fill its front.</param>
        public static PendingPlan From(IReadOnlyList<PlanSegment> segments, MissionTime now, Span<int> order)
        {
            var length = 0;
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.Start < now) continue;
                var at = length++;
                while (at > 0 && Compare(segments[order[at - 1]], segment) > 0)
                {
                    order[at] = order[at - 1];
                    at--;
                }
                order[at] = i;
            }
            return new PendingPlan(segments, order[..length]);
        }

        private static int Compare(PlanSegment a, PlanSegment b)
        {
            var byStart = a.Start.Milliseconds.CompareTo(b.Start.Milliseconds);
            return byStart != 0 ? byStart : string.CompareOrdinal(a.Id, b.Id);
        }
    }
}
