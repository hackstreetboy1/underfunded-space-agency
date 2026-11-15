using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// Precise instrument readings for the ground (E8, X8): what a sensor channel shows, read through its sensor model (bias,
/// noise, quantization; nothing while the sensor is failed, isolated, detached or unpowered), never the true value. Serves
/// <c>&lt;sensor&gt;.&lt;channel&gt;</c> for the <c>session.json</c> <c>telemetry</c> channels to
/// <see cref="Knowledge.TelemetryReadingSampler"/> (every other path reads the composed state), and reads a fact's sensor for
/// the <c>telemetry_dump</c> command. Uses its own noise stream, so glance-read noise is unchanged by telemetry.
/// </summary>
internal sealed class InstrumentTelemetry : IStateQuery, IHashContributor
{
    public const string NoiseStreamName = "telemetry_noise";

    private readonly VehicleSet _vehicles;
    private readonly IStateQuery _state;
    private readonly FactValueReader _reader;
    private readonly Pcg32 _noise;
    private readonly Dictionary<string, (int Vehicle, int Sensor, int Channel)> _channels = new(StringComparer.Ordinal);

    public InstrumentTelemetry(VehicleSet vehicles, IStateQuery state, FactValueReader reader, Pcg32 noise)
    {
        _vehicles = vehicles;
        _state = state;
        _reader = reader;
        _noise = noise;
    }

    /// <summary>Serves <paramref name="path"/> (<c>&lt;sensor component&gt;.&lt;channel field&gt;</c>) as an instrument reading.</summary>
    /// <returns>False when the path names no sensor channel.</returns>
    public bool AddChannel(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var dot = path.LastIndexOf('.');
        if (dot <= 0 || !_vehicles.TryFind(path[..dot], out var vehicle, out var sensor)) return false;
        if (_vehicles[vehicle].Layout[sensor].Sensor is not { } spec) return false;
        var field = path[(dot + 1)..];
        for (var c = 0; c < spec.Channels.Count; c++)
        {
            if (spec.Channels[c].Field != field) continue;
            _channels[path] = (vehicle, sensor, c);
            return true;
        }
        return false;
    }

    public bool TryGet(string path, out StateValue value)
    {
        if (!_channels.TryGetValue(path, out var channel)) return _state.TryGet(path, out value);
        value = Read(channel.Vehicle, channel.Sensor, channel.Channel) is { } reading ? reading : StateValue.None;
        return true;
    }

    public bool IsKnownPath(string path) => _channels.ContainsKey(path) || _state.IsKnownPath(path);

    /// <summary>One reading of <paramref name="fact"/>'s sensor channel; null when the fact has none or the sensor shows nothing.</summary>
    public double? ReadFact(FactDefinition fact) =>
        _reader.TryGetSensorChannel(fact, out var vehicle, out var sensor, out var channel) ? Read(vehicle, sensor, channel) : null;

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(unchecked((long)_noise.State));
    }

    private double? Read(int vehicle, int sensor, int channel)
    {
        var sensors = _vehicles.Sensors(vehicle);
        if (!sensors.IsOnline(sensor)) return null;
        var measures = _vehicles[vehicle].Layout[sensor].Sensor!.Channels[channel].Measures;
        if (!_state.TryGet(measures, out var truth) || truth.Kind != StateValueKind.Number) return null;
        var reading = sensors.Read(sensor, truth.AsNumber(), _noise);
        return reading.Available && double.IsFinite(reading.Value) ? reading.Value : null;
    }
}
