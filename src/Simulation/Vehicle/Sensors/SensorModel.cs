using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Vehicle.Sensors;

/// <summary>What a sensor shows: nothing when offline, otherwise truth with bias, noise and quantization applied.</summary>
public readonly record struct SensorReading(bool Available, double Value)
{
    public static readonly SensorReading Offline = new(false, 0);
}

/// <summary>
/// Applies sensor health, bias, Gaussian noise and quantization to true values. Sensors never change truth.
/// Use one <see cref="Pcg32"/> stream named <see cref="NoiseStream"/> for all sensor reads.
/// </summary>
public sealed class SensorModel(VehicleState state)
{
    public const string NoiseStream = "sensor_noise";

    private readonly VehicleState _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>Attached, not failed or isolated, and powered if it draws power.</summary>
    public bool IsOnline(int sensor) =>
        _state.IsOperable(sensor) && (_state.Layout[sensor].Load is null || _state[sensor].Powered);

    /// <summary>Reads <paramref name="trueValue"/> through the sensor.</summary>
    /// <param name="noise">The <see cref="NoiseStream"/> generator; two draws per noisy reading, none when offline.</param>
    public SensorReading Read(int sensor, double trueValue, Pcg32 noise)
    {
        ArgumentNullException.ThrowIfNull(noise);
        var spec = _state.Layout[sensor].Sensor
                   ?? throw new InvalidOperationException($"'{_state.Layout.IdOf(sensor)}' is not a sensor.");
        if (!IsOnline(sensor)) return SensorReading.Offline;

        var value = trueValue + _state[sensor].Bias;
        var std = _state[sensor].Condition == ComponentCondition.Degraded ? spec.NoiseStd * spec.DegradedNoiseFactor : spec.NoiseStd;
        if (std > 0) value += std * StandardNormal(noise);
        if (spec.Quantization > 0) value = Math.Round(value / spec.Quantization, MidpointRounding.AwayFromZero) * spec.Quantization;
        return new SensorReading(true, value);
    }

    /// <summary>Reads channel <paramref name="channel"/>, taking the true value from <paramref name="truth"/>.</summary>
    /// <exception cref="UnknownStatePathException">The measured path is not served.</exception>
    public SensorReading Read(int sensor, int channel, IStateQuery truth, Pcg32 noise)
    {
        ArgumentNullException.ThrowIfNull(truth);
        if (!IsOnline(sensor)) return SensorReading.Offline;
        var measures = _state.Layout[sensor].Sensor!.Channels[channel].Measures;
        return Read(sensor, truth.GetNumber(measures), noise);
    }

    public SensorReading Read(ComponentId sensor, double trueValue, Pcg32 noise) => Read(_state.Layout.IndexOf(sensor), trueValue, noise);

    // Box-Muller; 1 - u keeps the logarithm's argument in (0, 1].
    private static double StandardNormal(Pcg32 rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
