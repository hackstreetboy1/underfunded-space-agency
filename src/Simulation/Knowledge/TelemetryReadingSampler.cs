using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Knowledge;

/// <summary>One instrument downlinked as precise readings.</summary>
/// <param name="TelemetryPath">The sensor's reported value (what the instrument shows, not the true quantity).</param>
/// <param name="ValidMax">Readings above this are outside the instrument's range and are not reported (cabin altimeter: 30 km).</param>
public sealed record TelemetryChannel(FactId Fact, string TelemetryPath, double? ValidMax = null);

/// <summary>
/// Samples instrument channels and records each in-range number as a telemetry reading (unlike
/// <see cref="DownlinkSampler"/>, which keeps only band words). Used for the entry accelerometer and cabin altimeter.
/// </summary>
public sealed class TelemetryReadingSampler : ISimSystem
{
    private readonly KnowledgeBase _knowledge;
    private readonly IStateQuery _state;
    private readonly IExpression? _active;
    private readonly FactDefinition[] _facts;
    private readonly TelemetryChannel[] _channels;

    /// <param name="active">When given, readings flow only while it evaluates true (e.g. the entry phase).</param>
    /// <exception cref="ArgumentException">A channel names a fact that is not a number fact allowing the telemetry method.</exception>
    public TelemetryReadingSampler(
        KnowledgeBase knowledge,
        FactCatalog catalog,
        IStateQuery state,
        IReadOnlyList<TelemetryChannel> channels,
        IExpression? active = null,
        int periodTicks = SimulationClock.TicksPerSecond)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentOutOfRangeException.ThrowIfLessThan(periodTicks, 1);
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _active = active;
        PeriodTicks = periodTicks;
        _channels = [.. channels];
        _facts = new FactDefinition[_channels.Length];
        for (var i = 0; i < _channels.Length; i++)
        {
            var channel = _channels[i];
            if (!catalog.TryResolve(channel.Fact, out var fact) || fact.Kind != FactValueKind.Number || !fact.Allows(ObservationMethod.Telemetry))
                throw new ArgumentException($"Channel '{channel.Fact}' needs a number fact that allows telemetry.", nameof(channels));
            _facts[i] = fact;
        }
    }

    public int PeriodTicks { get; }

    public void Step(in StepContext context)
    {
        if (_active is not null && !_active.IsTrue(_state)) return;
        for (var i = 0; i < _channels.Length; i++)
        {
            var channel = _channels[i];
            if (!_state.TryGet(channel.TelemetryPath, out var value) || value.Kind != StateValueKind.Number) continue;
            var reading = value.AsNumber();
            if (!double.IsFinite(reading) || reading > channel.ValidMax) continue;
            _knowledge.ReportTelemetryReading(_facts[i], value, context.Now);
        }
    }
}
