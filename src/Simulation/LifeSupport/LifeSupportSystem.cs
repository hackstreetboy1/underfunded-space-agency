using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Atmosphere;
using Usa.Simulation.LifeSupport.Config;
using Usa.Simulation.LifeSupport.Consumables;
using Usa.Simulation.LifeSupport.Thermal;

namespace Usa.Simulation.LifeSupport;

/// <summary>
/// Steps thermal, cabin atmosphere and provisions once per second in that order (cabin air temperature comes from
/// the freshly stepped thermal node), routes crew metabolism into the cabins and their thermal nodes, and publishes
/// threshold events. All cross-area exchange goes through <see cref="ILifeSupportEnvironment"/>.
/// </summary>
public sealed class LifeSupportSystem : ISimSystem, IHashContributor
{
    private readonly ILifeSupportEnvironment _environment;
    private readonly EventBus _events;
    private readonly MetabolicModel _metabolic;
    private readonly CabinAtmosphere[] _cabins;
    private readonly int[] _cabinNode;
    private readonly double[] _cabinO2KgPerS;
    private readonly double[] _cabinCo2KgPerS;
    private readonly ThermalBand[] _lastBands;
    private readonly bool[] _lastO2Short;
    private readonly bool[] _lastBreakthrough;
    private readonly int[] _lastReplaced;
    private bool _foodExhausted;
    private bool _waterExhausted;

    public LifeSupportSystem(
        LifeSupportConfig config,
        ThermalNetwork thermal,
        IEnumerable<CabinAtmosphere> cabins,
        Provisions provisions,
        ILifeSupportEnvironment environment,
        EventBus events)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(cabins);
        Thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
        Provisions = provisions ?? throw new ArgumentNullException(nameof(provisions));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _metabolic = new MetabolicModel(config.Metabolic);

        _cabins = cabins.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        _cabinNode = new int[_cabins.Length];
        var errors = new DefinitionErrors();
        for (var c = 0; c < _cabins.Length; c++)
        {
            _cabinNode[c] = thermal.IndexOfNode(_cabins[c].ThermalNode);
            if (_cabinNode[c] < 0)
                errors.Add($"cabin {_cabins[c].Id}", "$.cabin.thermalNode", $"Unknown thermal node '{_cabins[c].ThermalNode}'.");
            if (c > 0 && string.Equals(_cabins[c].Id, _cabins[c - 1].Id, StringComparison.Ordinal))
                errors.Add($"cabin {_cabins[c].Id}", "$.cabin.id", "Cabin id is used twice.");
        }
        errors.ThrowIfAny();

        _cabinO2KgPerS = new double[_cabins.Length];
        _cabinCo2KgPerS = new double[_cabins.Length];
        _lastBands = new ThermalBand[thermal.NodeCount];
        for (var i = 0; i < _lastBands.Length; i++) _lastBands[i] = thermal.Band(i);
        _lastO2Short = new bool[_cabins.Length];
        _lastBreakthrough = _cabins.Select(c => c.ScrubberCapacityFactor < 1).ToArray();
        _lastReplaced = _cabins.Select(c => c.CanistersReplaced).ToArray();
        Frame = new LifeSupportFrame(thermal.CreateInputs(), _cabins.Length, provisions.CrewCount);
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public ThermalNetwork Thermal { get; }

    public IReadOnlyList<CabinAtmosphere> Cabins => _cabins;

    public Provisions Provisions { get; }

    public LifeSupportFrame Frame { get; }

    /// <summary>Builds every model from a vehicle spec; <paramref name="resourceMargin"/> is <c>difficulty.resource_margin</c>.</summary>
    public static LifeSupportSystem Create(
        LifeSupportConfig config,
        LifeSupportSpec spec,
        IEnumerable<CrewId> crew,
        double resourceMargin,
        ILifeSupportEnvironment environment,
        EventBus events)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(spec);
        var thermal = new ThermalNetwork(spec.Thermal, config.Thermal);
        var cabins = new List<CabinAtmosphere>(spec.Cabins.Count);
        foreach (var cabinSpec in spec.Cabins)
        {
            var node = thermal.IndexOfNode(cabinSpec.ThermalNode);
            var initialTempC = node >= 0 ? thermal.TemperatureC(node) : 20.0;
            cabins.Add(new CabinAtmosphere(cabinSpec, config.Regulator, config.Scrubber, initialTempC));
        }
        var provisions = new Provisions(spec.Provisions.WithResourceMargin(resourceMargin), crew, config.Rations, config.Deficits);
        return new LifeSupportSystem(config, thermal, cabins, provisions, environment, events);
    }

    /// <returns>The cabin index, or -1.</returns>
    public int IndexOfCabin(string id)
    {
        for (var c = 0; c < _cabins.Length; c++)
        {
            if (string.Equals(_cabins[c].Id, id, StringComparison.Ordinal)) return c;
        }
        return -1;
    }

    public void Step(in StepContext context)
    {
        var now = context.Now;
        var dt = context.DeltaSeconds;
        _environment.ReadInputs(now, Frame);

        AccumulateMetabolism();
        Thermal.Step(dt, Frame.Thermal);

        var results = Frame.WritableCabinResults;
        var fan = Frame.FanAirflowFraction;
        var supply = Frame.O2SupplyAvailableKg;
        var regulator = Frame.RegulatorEnabled;
        for (var c = 0; c < _cabins.Length; c++)
        {
            var input = new CabinStepInput(
                Thermal.TemperatureC(_cabinNode[c]), fan[c], supply[c], regulator[c], _cabinO2KgPerS[c], _cabinCo2KgPerS[c]);
            results[c] = _cabins[c].Step(dt, input);
        }

        Frame.ProvisionsResult = Provisions.Step(dt, Frame.CrewAlive, Frame.WaterIncomeL);

        PublishTransitions(now);
        _environment.ApplyOutputs(now, Frame);
    }

    public void WriteHash(StateHashWriter writer)
    {
        Thermal.WriteHash(writer);
        foreach (var cabin in _cabins) cabin.WriteHash(writer);
        Provisions.WriteHash(writer);
    }

    private void AccumulateMetabolism()
    {
        Array.Clear(_cabinO2KgPerS);
        Array.Clear(_cabinCo2KgPerS);
        var crewHeat = Frame.Thermal.CrewHeatW;
        crewHeat.Clear();

        var activities = Frame.CrewActivities;
        var cabinOf = Frame.CrewCabin;
        var alive = Frame.CrewAlive;
        for (var i = 0; i < activities.Length; i++)
        {
            var cabin = cabinOf[i];
            if (!alive[i] || cabin < 0 || cabin >= _cabins.Length) continue;
            var rate = _metabolic.RateFor(activities[i]);
            _cabinO2KgPerS[cabin] += rate.O2KgPerS;
            _cabinCo2KgPerS[cabin] += rate.Co2KgPerS;
            crewHeat[_cabinNode[cabin]] += rate.HeatW;
        }
    }

    private void PublishTransitions(MissionTime now)
    {
        for (var i = 0; i < _lastBands.Length; i++)
        {
            var band = Thermal.Band(i);
            if (band == _lastBands[i]) continue;
            _lastBands[i] = band;
            _events.Publish(new ThermalBandChanged(now, Thermal.NodeId(i), band, Thermal.TemperatureC(i)));
        }

        for (var c = 0; c < _cabins.Length; c++)
        {
            var cabin = _cabins[c];
            if (cabin.O2SupplyShort != _lastO2Short[c])
            {
                _lastO2Short[c] = cabin.O2SupplyShort;
                _events.Publish(new CabinO2SupplyChanged(now, cabin.Id, cabin.O2SupplyShort));
            }
            if (cabin.CanistersReplaced != _lastReplaced[c])
            {
                _lastReplaced[c] = cabin.CanistersReplaced;
                _events.Publish(new LiOhCanisterReplaced(now, cabin.Id));
            }
            var breakthrough = cabin.ScrubberCapacityFactor < 1;
            if (breakthrough != _lastBreakthrough[c])
            {
                _lastBreakthrough[c] = breakthrough;
                if (breakthrough) _events.Publish(new LiOhBreakthroughReached(now, cabin.Id, cabin.LiOhPct));
            }
        }

        var foodExhausted = Provisions.FoodKcal <= 0;
        if (foodExhausted && !_foodExhausted) _events.Publish(new ProvisionExhausted(now, "provisions.food_kcal"));
        _foodExhausted = foodExhausted;
        var waterExhausted = Provisions.WaterL <= 0;
        if (waterExhausted && !_waterExhausted) _events.Publish(new ProvisionExhausted(now, "provisions.water_l"));
        _waterExhausted = waterExhausted;
    }
}
