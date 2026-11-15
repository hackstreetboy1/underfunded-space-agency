using Usa.Simulation.Crew;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;
using Usa.Simulation.Vehicle;
using Usa.Simulation.Vehicle.Definitions;
using Usa.Simulation.Vehicle.Sensors;

namespace Usa.Simulation.Session;

/// <summary>
/// What a crew member reads for a fact when a glance or task produces an observation. facts.json names the source component
/// and, where the source alone cannot give it, a <c>valuePath</c>; the value is resolved by these rules, in order:
/// <list type="number">
/// <item>Crew self-reports: the member's health band. Component condition facts: the true condition, with the fault after a colon.</item>
/// <item>Facts with a <c>valuePath</c> in facts.json: that expression, read as truth.</item>
/// <item>A sensor source (or a sensor listed on the source component): the channel whose field equals the fact id's last
/// segment or ends it after an underscore, or the only channel; read through <see cref="SensorModel"/> (bias, noise,
/// quantization; nothing when offline). Text channels report their text while the sensor is online.</item>
/// <item>Any other component: the field named by the fact id's last segment, else the family's default field
/// (tank <c>quantity_kg</c>, fuel cell <c>output_kw</c>, battery <c>energy_kwh</c>, bus <c>voltage_v</c>, docking <c>load_pct</c>);
/// a thruster group reports its ring test word ('nominal' | 'no_response' | 'failed').</item>
/// </list>
/// </summary>
internal sealed class FactValueReader
{
    private static readonly (ComponentFamily Family, string Field)[] DefaultFields =
    [
        (ComponentFamily.Tank, ComponentFields.QuantityKg),
        (ComponentFamily.FuelCell, ComponentFields.OutputKw),
        (ComponentFamily.Battery, ComponentFields.EnergyKwh),
        (ComponentFamily.Bus, ComponentFields.VoltageV),
        (ComponentFamily.Docking, ComponentFields.LoadPct),
    ];

    private readonly FactCatalog _catalog;
    private readonly VehicleSet _vehicles;
    private readonly IStateQuery _state;
    private readonly CrewRoster _roster;
    private readonly IExpressionCompiler _compiler;
    private readonly Pcg32 _noise;
    private readonly Dictionary<FactId, Source> _sources = [];

    public FactValueReader(FactCatalog catalog, VehicleSet vehicles, IStateQuery state, CrewRoster roster, IExpressionCompiler compiler, Pcg32 noise)
    {
        _catalog = catalog;
        _vehicles = vehicles;
        _state = state;
        _roster = roster;
        _compiler = compiler;
        _noise = noise;
    }

    public ulong NoiseState => _noise.State;

    /// <summary>The report for <paramref name="fact"/>, or null when nothing can be read (sensor offline, unknown source).</summary>
    public ObservationReport? Read(FactId fact, CrewId observer, ObservationMethod method, MissionTime now, IReadOnlyList<ComponentId> scope)
    {
        if (!_catalog.TryResolve(fact, out var definition)) return null;
        var source = SourceOf(definition);
        if (source.Problem is not null) return null;

        double? number = null;
        string? text = null;
        switch (source.Kind)
        {
            case SourceKind.CrewBand:
                if (definition.SourceCrew is not { } crew || !_roster.TryFind(crew, out var member)) return null;
                text = CrewRoster.BandName(member.Band);
                break;
            case SourceKind.Condition:
                if (!_vehicles.TryFind(source.Component.Value, out var v, out var i)) return null;
                ref readonly var state = ref _vehicles[v][i];
                text = state.Fault.Length == 0 ? ComponentFields.ConditionName(state.Condition) : $"{ComponentFields.ConditionName(state.Condition)}:{state.Fault}";
                break;
            case SourceKind.RingTest:
                text = RingTestWord(source.Component.Value);
                break;
            case SourceKind.Sensor:
                if (!ReadSensor(source, out number, out text)) return null;
                break;
            default:
                var value = source.Expression!.Evaluate(_state);
                if (value.Kind == StateValueKind.Text) text = value.AsText();
                else number = value.AsNumber();
                break;
        }

        if (definition.Kind == FactValueKind.Text && text is null && number is { } n) text = n.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return new ObservationReport(fact, definition.Kind == FactValueKind.Number ? number : null, text, observer, method, 1.0, now, scope);
    }

    /// <summary>The true numeric value behind <paramref name="fact"/> (no sensor bias or noise); null when it has none.</summary>
    public double? Truth(FactDefinition fact)
    {
        var source = SourceOf(fact);
        StateValue value;
        switch (source.Kind)
        {
            case SourceKind.Expression:
                value = source.Expression!.Evaluate(_state);
                break;
            case SourceKind.Sensor:
                var measures = _vehicles[source.Vehicle].Layout[source.Sensor].Sensor!.Channels[source.Channel].Measures;
                if (!_state.TryGet(measures, out value)) return null;
                break;
            default:
                return null;
        }
        return value.Kind is StateValueKind.Number ? value.AsNumber() : null;
    }

    /// <summary>
    /// The sensor channel a telemetry reading of <paramref name="fact"/> goes through: the fact's sensor source (or a sensor
    /// listed on its source component), never a <c>valuePath</c> or another field, so telemetry never reads truth.
    /// </summary>
    /// <returns>False when the fact has no sensor channel.</returns>
    public bool TryGetSensorChannel(FactDefinition fact, out int vehicle, out int sensor, out int channel)
    {
        ArgumentNullException.ThrowIfNull(fact);
        vehicle = sensor = channel = -1;
        if (fact.SourceKind != FactSourceKind.Sensor || fact.SourceComponent is not { } component) return false;
        if (SensorSource(fact, component) is not { Kind: SourceKind.Sensor } source) return false;
        (vehicle, sensor, channel) = (source.Vehicle, source.Sensor, source.Channel);
        return true;
    }

    /// <summary>Why <paramref name="fact"/> has no readable value, or null when it has one (load-time check).</summary>
    public string? Problem(FactDefinition fact) => SourceOf(fact).Problem;

    private bool ReadSensor(Source source, out double? number, out string? text)
    {
        number = null;
        text = null;
        var vehicle = source.Vehicle;
        var sensors = _vehicles.Sensors(vehicle);
        if (!sensors.IsOnline(source.Sensor)) return false;
        var measures = _vehicles[vehicle].Layout[source.Sensor].Sensor!.Channels[source.Channel].Measures;
        if (!_state.TryGet(measures, out var truth)) return false;
        if (truth.Kind == StateValueKind.Text)
        {
            text = truth.AsText();
            return true;
        }
        var reading = sensors.Read(source.Sensor, truth.AsNumber(), _noise);
        if (!reading.Available) return false;
        number = reading.Value;
        return true;
    }

    private string RingTestWord(string group)
    {
        if (_state.TryGet(group + "." + ComponentFields.Condition, out var condition) && condition.Kind == StateValueKind.Text &&
            condition.AsText() == ComponentFields.ConditionName(ComponentCondition.Failed))
            return "failed";
        return _state.TryGet(group + "." + ComponentFields.DriversPowered, out var powered) && powered.AsBool() ? "nominal" : "no_response";
    }

    private Source SourceOf(FactDefinition fact)
    {
        if (_sources.TryGetValue(fact.Id, out var cached)) return cached;
        var source = Create(fact);
        _sources[fact.Id] = source;
        return source;
    }

    private Source Create(FactDefinition fact)
    {
        if (fact.SourceKind == FactSourceKind.Crew) return new Source(SourceKind.CrewBand);
        if (fact.ConfirmsComponent is { } confirms) return new Source(SourceKind.Condition) { Component = confirms };
        if (fact.ValuePath is { } valuePath) return Compile(fact, valuePath);
        if (fact.SourceComponent is not { } component) return Missing(fact, "has no source component");
        if (!_vehicles.TryFind(component.Value, out var vehicle, out var index)) return Missing(fact, $"source '{component}' is not a vehicle component");

        var layout = _vehicles[vehicle].Layout;
        var definition = layout[index];
        var segment = fact.Id.Value[(fact.Id.Value.LastIndexOf('.') + 1)..];

        if (fact.SourceKind == FactSourceKind.Sensor && SensorSource(fact, component) is { } sensorSource) return sensorSource;

        if (definition.Family == ComponentFamily.ThrusterGroup) return new Source(SourceKind.RingTest) { Component = component };
        if (_state.IsKnownPath($"{component.Value}.{segment}")) return Compile(fact, $"@{component.Value}.{segment}");
        foreach (var (family, field) in DefaultFields)
        {
            if (definition.Family == family && _state.IsKnownPath($"{component.Value}.{field}")) return Compile(fact, $"@{component.Value}.{field}");
        }
        return Missing(fact, $"no field of '{component}' ({definition.Family}) gives its value");
    }

    /// <summary>The sensor channel of a sensor fact's source component; null when the component neither is nor lists a sensor.</summary>
    private Source? SensorSource(FactDefinition fact, ComponentId component)
    {
        if (!_vehicles.TryFind(component.Value, out var vehicle, out var index)) return null;
        var layout = _vehicles[vehicle].Layout;
        var definition = layout[index];
        var segment = fact.Id.Value[(fact.Id.Value.LastIndexOf('.') + 1)..];
        if (definition.Sensor is not null && MatchChannel(definition.Sensor, segment) is var own and >= 0)
            return new Source(SourceKind.Sensor) { Vehicle = vehicle, Sensor = index, Channel = own };
        foreach (var sensorId in definition.Sensors)
        {
            if (!layout.TryGetIndex(sensorId.Value, out var sensor) || layout[sensor].Sensor is not { } spec) continue;
            var channel = MatchChannel(spec, segment);
            if (channel >= 0) return new Source(SourceKind.Sensor) { Vehicle = vehicle, Sensor = sensor, Channel = channel };
        }
        return definition.Sensor is not null ? Missing(fact, $"no channel of sensor '{component}' matches '{segment}'") : null;
    }

    private Source Compile(FactDefinition fact, string source)
    {
        try
        {
            var expression = _compiler.Compile(source);
            foreach (var path in expression.Paths)
            {
                if (!_state.IsKnownPath(path)) return Missing(fact, $"value path '{path}' is unknown");
            }
            return new Source(SourceKind.Expression) { Expression = expression };
        }
        catch (ExpressionException ex)
        {
            return Missing(fact, ex.Message);
        }
    }

    private static int MatchChannel(SensorSpec sensor, string segment)
    {
        for (var c = 0; c < sensor.Channels.Count; c++)
        {
            var field = sensor.Channels[c].Field;
            if (segment == field || segment.EndsWith("_" + field, StringComparison.Ordinal)) return c;
        }
        return sensor.Channels.Count == 1 ? 0 : -1;
    }

    private static Source Missing(FactDefinition fact, string why) => new(SourceKind.Missing) { Problem = $"Fact '{fact.Id}': {why}." };

    private enum SourceKind
    {
        Missing,
        CrewBand,
        Condition,
        RingTest,
        Sensor,
        Expression,
    }

    private sealed record Source(SourceKind Kind)
    {
        public ComponentId Component { get; init; }

        public int Vehicle { get; init; } = -1;

        public int Sensor { get; init; } = -1;

        public int Channel { get; init; } = -1;

        public IExpression? Expression { get; init; }

        public string? Problem { get; init; }
    }
}
