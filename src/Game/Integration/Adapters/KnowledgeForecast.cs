using System.Globalization;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Simulation.Forecast;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// The forecast pill (GDD 6.4) computed by <see cref="SurvivalForecast"/> from what the ground knows, unlike the session's own
/// forecast (which falls back to truth for unobserved quantities and uses the true drawdown).
/// <para>Resources are the numeric facts a model may estimate. Quantity: the latest crew measurement (fresh or stale), else the
/// model estimate (stale), else the launch loadout from the vehicle data (unknown). Consumption: the slope of the fact's own
/// measurements, else the drawdown from the loadout to the latest measurement since mission start (stale), else unknown.</para>
/// </summary>
public sealed class KnowledgeForecast
{
    private const string FoodRationPath = "provisions.food_ration_pct";
    private const string WaterRationPath = "provisions.water_ration_pct";

    private readonly SurvivalForecast _forecast;
    private readonly KnowledgeBase _knowledge;
    private readonly KnowledgeReadouts _readouts;
    private readonly IStateQuery _state;
    private readonly AdapterText _text;
    private readonly MissionTime _start;
    private readonly long _periodMs;
    private readonly List<(FactDefinition Fact, double? LoadoutQuantity)> _resources = [];
    private ForecastSnapshot? _latest;
    private long _computedMs = long.MinValue;
    private int _computedCrew = -1;

    public KnowledgeForecast(MissionSession session, KnowledgeReadouts readouts, AdapterText? text = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(readouts);
        _text = text ?? AdapterText.Default;
        _forecast = new SurvivalForecast(session.Data.Forecast);
        _knowledge = session.Knowledge;
        _readouts = readouts;
        _state = session.State;
        _start = session.Data.Scenario.Mission.StartTime;
        _periodMs = Math.Max(1000, (long)Math.Round(session.Data.Forecast.RecomputePeriodS * 1000));

        var components = new Dictionary<ComponentId, ComponentDefinition>();
        ProvisionsDefinition? provisions = null;
        foreach (var vehicle in session.Data.Vehicles)
        {
            provisions ??= vehicle.Provisions;
            foreach (var component in vehicle.Components) components[component.Id] = component;
        }
        foreach (var fact in session.Data.Facts.Facts.Where(f => f.Allows(ObservationMethod.Model) && f.Kind == FactValueKind.Number).OrderBy(f => f.Id))
            _resources.Add((fact, Loadout(fact, components, provisions)));
    }

    /// <summary>
    /// Recomputed every <c>recomputePeriodS</c> of MET, at once when the crew count changes (a death changes every
    /// consumption at once), or when <paramref name="force"/> is set.
    /// </summary>
    public ForecastSnapshot? Latest(MissionTime now, int crewAlive, bool force = false)
    {
        if (!force && _latest is not null && crewAlive == _computedCrew && now.Milliseconds - _computedMs < _periodMs) return _latest;
        _computedMs = now.Milliseconds;
        _computedCrew = crewAlive;
        _latest = Compute(now, crewAlive);
        return _latest;
    }

    private ForecastSnapshot? Compute(MissionTime now, int crewAlive)
    {
        var inputs = new List<ForecastResource>(_resources.Count);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (fact, loadout) in _resources)
        {
            if (Resource(fact, loadout, now) is not { } resource) continue;
            inputs.Add(resource);
            labels[fact.Id.Value] = fact.Label;
        }
        if (inputs.Count == 0) return null;

        var result = _forecast.Compute(new ForecastInput(now, inputs)
        {
            CrewCount = crewAlive,
            FoodRationPct = Number(FoodRationPath, 100),
            WaterRationPct = Number(WaterRationPath, 100),
        });

        var lines = result.Resources.Select(r => new ForecastResourceLine(Label(labels, r.Id), r.LowH, r.HighH, AdapterMaps.Knowledge(r.WorstKnowledge))).ToArray();
        return new ForecastSnapshot(
            result.ComputedAt,
            result.SafeAutonomy.LowH,
            result.SafeAutonomy.HighH,
            result.BeyondHorizon ? string.Empty : Label(labels, result.LimitingFactorId),
            Cap(AdapterMaps.Confidence(result.Confidence), inputs))
        {
            ConfidenceReason = ConfidenceReason(result, labels),
            Coverage = AdapterMaps.Coverage(result.Coverage),
            Resources = lines,
            Assumptions = result.Assumptions.Select(a => Sentence(a, labels)).Where(s => s.Length > 0).ToArray(),
        };
    }

    /// <summary>
    /// A forecast is never more confident than its weakest input: an unknown quantity or use rate caps it at low, a stale one at
    /// medium (an unknown use rate counts as zero in the projection, which would otherwise read as a confident open range).
    /// </summary>
    private static ForecastConfidenceLevel Cap(ForecastConfidenceLevel confidence, List<ForecastResource> inputs)
    {
        var worst = InputKnowledge.Fresh;
        foreach (var input in inputs)
        {
            if (input.QuantityKnowledge > worst) worst = input.QuantityKnowledge;
            if (input.ConsumptionKnowledge > worst) worst = input.ConsumptionKnowledge;
        }
        var cap = worst switch
        {
            InputKnowledge.Unknown => ForecastConfidenceLevel.Low,
            InputKnowledge.Stale => ForecastConfidenceLevel.Medium,
            _ => ForecastConfidenceLevel.High,
        };
        return confidence < cap ? confidence : cap;
    }

    private ForecastResource? Resource(FactDefinition fact, double? loadout, MissionTime now)
    {
        var known = _knowledge.Describe(fact.Id);
        double quantity;
        InputKnowledge quantityKnowledge;
        if (known.Latest is { Value: { } measured } latest)
        {
            quantity = measured;
            quantityKnowledge = fact.IsStale(latest.Time, now) ? InputKnowledge.Stale : InputKnowledge.Fresh;
        }
        else if (known.Estimate is { Value: { } estimated })
        {
            quantity = estimated;
            quantityKnowledge = InputKnowledge.Stale;
        }
        else if (loadout is { } launched)
        {
            quantity = launched;
            quantityKnowledge = InputKnowledge.Unknown;
        }
        else
        {
            return null;
        }

        var (perHour, consumptionKnowledge) = Consumption(fact, known, loadout, now);
        return new ForecastResource
        {
            Id = fact.Id.Value,
            Quantity = quantity,
            QuantityKnowledge = quantityKnowledge,
            ConsumptionPerH = perHour,
            ConsumptionKnowledge = consumptionKnowledge,
        };
    }

    private (double PerHour, InputKnowledge Knowledge) Consumption(FactDefinition fact, FactKnowledge known, double? loadout, MissionTime now)
    {
        if (_readouts.TrendPerHour(fact.Id) is { } trend)
        {
            var fresh = known.Latest is { } latest && !fact.IsStale(latest.Time, now);
            return (Math.Max(0, -trend), fresh ? InputKnowledge.Fresh : InputKnowledge.Stale);
        }
        if (known.Latest is { Value: { } measured } only && loadout is { } launched && only.Time > _start)
        {
            var hours = (only.Time.Milliseconds - _start.Milliseconds) / 3_600_000.0;
            return (Math.Max(0, (launched - measured) / hours), InputKnowledge.Stale);
        }
        return (0, InputKnowledge.Unknown);
    }

    /// <summary>What was loaded at launch, from the vehicle data (the ground's pre-flight figure, not a reading).</summary>
    private static double? Loadout(FactDefinition fact, Dictionary<ComponentId, ComponentDefinition> components, ProvisionsDefinition? provisions)
    {
        if (fact.ValuePath is { } path && path.Contains("provisions.water_l", StringComparison.Ordinal)) return provisions?.PotableWaterL;
        if (fact.Unit == "L" && fact.Id.Value.Contains("water", StringComparison.Ordinal)) return provisions?.PotableWaterL;
        if (fact.SourceComponent is not { } source || !components.TryGetValue(source, out var component)) return null;
        if (component.Tank is { } tank) return tank.InitialKg;
        if (component.Battery is { } battery) return battery.InitialKwh;
        if (component.Scrubber is not null && fact.Unit == "%") return 100;
        return null;
    }

    private double Number(string path, double fallback) =>
        _state.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? value.AsNumber() : fallback;

    private static string Label(Dictionary<string, string> labels, string id) => labels.TryGetValue(id, out var label) ? label : id;

    private string? ConfidenceReason(ForecastResult result, Dictionary<string, string> labels)
    {
        foreach (var resource in result.Resources)
        {
            if (resource.WorstKnowledge == InputKnowledge.Unknown)
                return _text.Format("console.forecastReasonNotChecked", ("resource", Label(labels, resource.Id)));
        }
        foreach (var resource in result.Resources)
        {
            if (resource.WorstKnowledge == InputKnowledge.Stale)
                return _text.Format("console.forecastReasonOld", ("resource", Label(labels, resource.Id)));
        }
        return null;
    }

    private string Sentence(ForecastAssumption assumption, Dictionary<string, string> labels)
    {
        var key = assumption.Key switch
        {
            ForecastAssumptionKeys.CrewCount => "forecastAssumeCrew",
            ForecastAssumptionKeys.FoodRation => "forecastAssumeFood",
            ForecastAssumptionKeys.WaterRation => "forecastAssumeWater",
            ForecastAssumptionKeys.QuantityStale => "forecastAssumeQuantityStale",
            ForecastAssumptionKeys.QuantityUnknown => "forecastAssumeQuantityUnknown",
            ForecastAssumptionKeys.ConsumptionStale => "forecastAssumeUseStale",
            ForecastAssumptionKeys.ConsumptionUnknown => "forecastAssumeUseUnknown",
            ForecastAssumptionKeys.ProductionStale => "forecastAssumeProductionStale",
            ForecastAssumptionKeys.ProductionUnknown => "forecastAssumeProductionUnknown",
            ForecastAssumptionKeys.PlanEnd => "forecastAssumePlanEnd",
            ForecastAssumptionKeys.BeyondHorizon => "forecastAssumeBeyondHorizon",
            _ => null,
        };
        if (key is null) return string.Empty;
        var value = assumption.Value;
        var whole = value.ToString("0", CultureInfo.InvariantCulture);
        return _text.Format("console." + key,
            ("resource", Label(labels, assumption.Subject)),
            ("count", whole),
            ("percent", whole),
            ("hours", assumption.Key == ForecastAssumptionKeys.PlanEnd ? value.ToString("0.#", CultureInfo.InvariantCulture) : whole));
    }
}
