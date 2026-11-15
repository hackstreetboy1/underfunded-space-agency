using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>
/// Samples a value once per MET minute (UI.md 6.4) into a 60-sample window. Minutes skipped between two
/// updates (a large compression jump) become gaps rather than invented values.
/// </summary>
public sealed class SparklineSampler
{
    public const int Capacity = 60;
    public const long SampleIntervalMs = 60_000;

    private readonly Queue<double?> _samples = new(Capacity);
    private long? _lastSlot;

    public SparklineSeries Series { get; private set; } = SparklineSeries.Empty;

    /// <param name="value">The value known now; null records a gap.</param>
    /// <returns>True when a new sample was taken (and <see cref="Series"/> replaced).</returns>
    public bool Sample(MissionTime now, double? value)
    {
        var slot = FloorDiv(now.Milliseconds, SampleIntervalMs);
        if (_lastSlot is { } last)
        {
            if (slot <= last) return false;
            var missed = slot - last - 1;
            if (missed >= Capacity) _samples.Clear();
            else for (var i = 0; i < missed; i++) Enqueue(null);
        }

        Enqueue(value);
        _lastSlot = slot;
        Series = new SparklineSeries(new MissionTime(slot * SampleIntervalMs), [.. _samples]);
        return true;
    }

    public void Reset()
    {
        _samples.Clear();
        _lastSlot = null;
        Series = SparklineSeries.Empty;
    }

    private void Enqueue(double? value)
    {
        if (_samples.Count == Capacity) _samples.Dequeue();
        _samples.Enqueue(value);
    }

    private static long FloorDiv(long value, long divisor)
    {
        var quotient = value / divisor;
        return value % divisor != 0 && value < 0 ? quotient - 1 : quotient;
    }
}
