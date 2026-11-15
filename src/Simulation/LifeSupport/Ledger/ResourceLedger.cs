using Usa.Simulation.Kernel;

namespace Usa.Simulation.LifeSupport.Ledger;

/// <summary>
/// Aggregates resource inventories and flows reported by the owning systems, keeps a 60-minute history sampled on
/// every whole minute of MET, and derives hourly trends for the UI and the survival forecast.
/// Resources are indexed in ordinal id order; look indices up once at setup with <see cref="IndexOf"/>.
/// </summary>
public sealed class ResourceLedger : IHashContributor
{
    public const int HistoryLength = 60;
    public const long SampleIntervalMs = 60_000;

    private readonly ResourceDefinition[] _definitions;
    private readonly double[] _quantity;
    private readonly double[] _productionPerS;
    private readonly double[] _consumptionPerS;
    private readonly double[] _history;
    private readonly long[] _sampleTimesMs = new long[HistoryLength];
    private int _historyHead;
    private int _historyCount;
    private long _lastSampleBucket = long.MinValue;

    public ResourceLedger(IEnumerable<ResourceDefinition> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _definitions = resources.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        for (var i = 1; i < _definitions.Length; i++)
        {
            if (string.Equals(_definitions[i].Id, _definitions[i - 1].Id, StringComparison.Ordinal))
                throw new ArgumentException($"Resource '{_definitions[i].Id}' is registered twice.", nameof(resources));
        }
        _quantity = _definitions.Select(r => r.InitialQuantity).ToArray();
        _productionPerS = new double[_definitions.Length];
        _consumptionPerS = new double[_definitions.Length];
        _history = new double[_definitions.Length * HistoryLength];
    }

    public int Count => _definitions.Length;

    /// <summary>Samples held per resource (all resources are sampled together), at most <see cref="HistoryLength"/>.</summary>
    public int HistoryCount => _historyCount;

    public ResourceDefinition Definition(int index) => _definitions[index];

    /// <returns>The resource index, or -1.</returns>
    public int IndexOf(string id)
    {
        for (var i = 0; i < _definitions.Length; i++)
        {
            if (string.Equals(_definitions[i].Id, id, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    /// <summary>Records the owner's current figures; call every step before <see cref="Sample"/>.</summary>
    public void Report(int index, double quantity, double productionPerS, double consumptionPerS)
    {
        _quantity[index] = quantity;
        _productionPerS[index] = productionPerS;
        _consumptionPerS[index] = consumptionPerS;
    }

    /// <summary>Appends one history sample per resource when MET has entered a new whole minute.</summary>
    public void Sample(MissionTime now)
    {
        var bucket = Math.DivRem(now.Milliseconds, SampleIntervalMs, out var remainder);
        if (remainder < 0) bucket--;
        if (bucket == _lastSampleBucket) return;
        _lastSampleBucket = bucket;

        for (var i = 0; i < _definitions.Length; i++) _history[i * HistoryLength + _historyHead] = _quantity[i];
        _sampleTimesMs[_historyHead] = now.Milliseconds;
        _historyHead = (_historyHead + 1) % HistoryLength;
        if (_historyCount < HistoryLength) _historyCount++;
    }

    public double Quantity(int index) => _quantity[index];

    /// <summary>Sample <paramref name="age"/> minutes back from the newest (0 = newest).</summary>
    public double HistoryAt(int index, int age) => _history[index * HistoryLength + Slot(age)];

    /// <summary>MET of the sample <paramref name="age"/> minutes back (the first sample can fall inside a minute).</summary>
    public MissionTime SampleTime(int age) => new(_sampleTimesMs[Slot(age)]);

    /// <summary>Copies the history oldest-first into <paramref name="destination"/>; returns the number written.</summary>
    public int CopyHistory(int index, Span<double> destination)
    {
        var count = Math.Min(_historyCount, destination.Length);
        for (var k = 0; k < count; k++) destination[k] = HistoryAt(index, count - 1 - k);
        return count;
    }

    /// <summary>Least-squares slope of the sampled history per hour; the instantaneous net rate before two samples exist.</summary>
    public double TrendPerHour(int index)
    {
        var n = _historyCount;
        if (n < 2) return (_productionPerS[index] - _consumptionPerS[index]) * 3600.0;

        var newestMs = _sampleTimesMs[Slot(0)];
        double sumX = 0, sumY = 0, sumXy = 0, sumXx = 0;
        for (var k = 0; k < n; k++)
        {
            var xHours = (_sampleTimesMs[Slot(k)] - newestMs) / 3_600_000.0;
            var y = HistoryAt(index, k);
            sumX += xHours;
            sumY += y;
            sumXy += xHours * y;
            sumXx += xHours * xHours;
        }
        var denominator = n * sumXx - sumX * sumX;
        return denominator == 0 ? 0 : (n * sumXy - sumX * sumY) / denominator;
    }

    public ResourceSnapshot Snapshot(int index)
    {
        var definition = _definitions[index];
        return new ResourceSnapshot(
            definition.Id, definition.Unit, _quantity[index], definition.Capacity,
            _productionPerS[index], _consumptionPerS[index], TrendPerHour(index), _historyCount);
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(_lastSampleBucket);
        writer.Write((long)_historyCount);
        for (var age = 0; age < _historyCount; age++) writer.Write(_sampleTimesMs[Slot(age)]);
        for (var i = 0; i < _definitions.Length; i++)
        {
            writer.Write(_definitions[i].Id);
            writer.Write(_quantity[i]);
            writer.Write(_productionPerS[i]);
            writer.Write(_consumptionPerS[i]);
            for (var age = 0; age < _historyCount; age++) writer.Write(HistoryAt(i, age));
        }
    }

    private int Slot(int age)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(age, _historyCount);
        return (_historyHead - 1 - age + HistoryLength) % HistoryLength;
    }
}
