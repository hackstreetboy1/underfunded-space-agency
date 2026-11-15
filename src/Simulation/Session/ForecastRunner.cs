using Usa.Simulation.Crew;
using Usa.Simulation.Forecast;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.LifeSupport.Consumables;

namespace Usa.Simulation.Session;

/// <summary>
/// Recomputes <see cref="SurvivalForecast"/> every <c>recomputePeriodS</c> of forecast.json.
/// <para>Resources are the facts the catalog lets a model estimate (method <c>model</c>), plus stowed food. Quantities come from
/// what the ground knows: the latest crew measurement (fresh or stale), else the true value marked unknown so the band is
/// widest. Consumption is the true drawdown since the previous run (the ground's consumption model), unknown on the first run.</para>
/// </summary>
internal sealed class ForecastRunner : ISimSystem, IHashContributor
{
    private const string FoodId = "provisions.food_kcal";

    private readonly SurvivalForecast _forecast;
    private readonly KnowledgeBase _knowledge;
    private readonly FactValueReader _reader;
    private readonly CrewRoster _roster;
    private readonly Provisions _provisions;
    private readonly FactDefinition[] _facts;
    private readonly double[] _previous;
    private long _previousMs = -1;

    public ForecastRunner(SurvivalForecast forecast, FactCatalog catalog, KnowledgeBase knowledge, FactValueReader reader, CrewRoster roster, Provisions provisions)
    {
        _forecast = forecast;
        _knowledge = knowledge;
        _reader = reader;
        _roster = roster;
        _provisions = provisions;
        _facts = catalog.Facts.Where(f => f.Allows(ObservationMethod.Model) && f.Kind == FactValueKind.Number).OrderBy(f => f.Id).ToArray();
        _previous = new double[_facts.Length + 1];
        PeriodTicks = Math.Max(1, (int)Math.Round(forecast.Config.RecomputePeriodS * SimulationClock.TicksPerSecond));
    }

    public int PeriodTicks { get; }

    public ForecastResult? Latest { get; private set; }

    public void Step(in StepContext context)
    {
        var hours = _previousMs < 0 ? 0 : (context.Now.Milliseconds - _previousMs) / 3_600_000.0;
        var resources = new List<ForecastResource>(_facts.Length + 1);
        for (var i = 0; i < _facts.Length; i++)
        {
            var truth = _reader.Truth(_facts[i]) ?? 0;
            resources.Add(Resource(_facts[i], truth, i, hours));
        }
        resources.Add(Consumption(FoodId, _provisions.FoodKcal, InputKnowledge.Fresh, _facts.Length, hours));

        _previousMs = context.Now.Milliseconds;
        Latest = _forecast.Compute(new ForecastInput(context.Now, resources)
        {
            CrewCount = _roster.AliveCount,
            FoodRationPct = _roster.FoodRationPct,
            WaterRationPct = _roster.WaterRationPct,
        });
    }

    public void WriteHash(StateHashWriter writer)
    {
        writer.Write(_previousMs);
        foreach (var value in _previous) writer.Write(value);
    }

    private ForecastResource Resource(FactDefinition fact, double truth, int slot, double hours)
    {
        var known = _knowledge.Describe(fact.Id);
        var (quantity, knowledge) = known.Latest is { Value: { } measured } latest
            ? (measured, fact.IsStale(latest.Time, _knowledge.Now) ? InputKnowledge.Stale : InputKnowledge.Fresh)
            : (truth, InputKnowledge.Unknown);
        return Consumption(fact.Id.Value, quantity, knowledge, slot, hours, truth);
    }

    private ForecastResource Consumption(string id, double quantity, InputKnowledge knowledge, int slot, double hours, double? truth = null)
    {
        var current = truth ?? quantity;
        var perHour = hours > 0 ? Math.Max(0, (_previous[slot] - current) / hours) : 0;
        _previous[slot] = current;
        return new ForecastResource
        {
            Id = id,
            Quantity = quantity,
            QuantityKnowledge = knowledge,
            ConsumptionPerH = perHour,
            ConsumptionKnowledge = hours > 0 ? InputKnowledge.Fresh : InputKnowledge.Unknown,
        };
    }
}
