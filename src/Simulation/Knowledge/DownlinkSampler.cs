using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Knowledge;

/// <summary>
/// Samples telemetry for every downlink fact and feeds it to the knowledge base, which keeps only the
/// coarse band word. Reads the owning areas' state through <see cref="IStateQuery"/>; never reveals components.
/// </summary>
public sealed class DownlinkSampler : ISimSystem
{
    private readonly KnowledgeBase _knowledge;
    private readonly IStateQuery _state;
    private readonly IExpression? _linkAvailable;
    private readonly FactDefinition[] _facts;

    /// <param name="linkAvailable">When given, telemetry flows only while it evaluates true (comms state).</param>
    public DownlinkSampler(KnowledgeBase knowledge, FactCatalog catalog, IStateQuery state, IExpression? linkAvailable = null, int periodTicks = SimulationClock.TicksPerSecond)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfLessThan(periodTicks, 1);
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _linkAvailable = linkAvailable;
        PeriodTicks = periodTicks;
        _facts = catalog.Facts.Where(f => f.Downlink is not null).ToArray();
    }

    public int PeriodTicks { get; }

    public void Step(in StepContext context)
    {
        if (_linkAvailable is not null && !_linkAvailable.IsTrue(_state)) return;
        for (var i = 0; i < _facts.Length; i++)
        {
            var fact = _facts[i];
            if (_state.TryGet(fact.Downlink!.TelemetryPath, out var telemetry)) _knowledge.ReportTelemetry(fact, telemetry, context.Now);
        }
    }
}
