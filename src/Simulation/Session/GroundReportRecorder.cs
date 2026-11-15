using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// Records the ground's reports (<see cref="FactSourceKind.Report"/> facts, e.g. <c>tandem.attitude.mode</c>,
/// <c>recovery.zone</c>) as telemetry observations once a second: a fact is recorded when its <c>valuePath</c>
/// (<see cref="GroundReportPaths"/>) has a value and the ground's knowledge of it is not yet known, has gone stale or differs.
/// A report with no value yet (empty text, none) is skipped. It keeps no state of its own: what was last reported is read
/// back from the knowledge base, so a resumed run records exactly the same observations.
/// </summary>
internal sealed class GroundReportRecorder : ISimSystem
{
    private readonly KnowledgeBase _knowledge;
    private readonly IStateQuery _state;
    private readonly FactDefinition[] _facts;
    private readonly IExpression[] _values;
    private readonly string[] _knownValuePaths;

    public GroundReportRecorder(KnowledgeBase knowledge, FactCatalog catalog, IStateQuery state, IExpressionCompiler compiler)
    {
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(compiler);
        _facts = [.. catalog.Facts.Where(f => f.SourceKind == FactSourceKind.Report).OrderBy(f => f.Id.Value, StringComparer.Ordinal)];
        _values = [.. _facts.Select(f => compiler.Compile(f.ValuePath!))];
        _knownValuePaths = [.. _facts.Select(f => $"{StatePaths.KnowledgeFact}{f.Id}.value")];
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    /// <summary>The report facts, ordinal by id.</summary>
    public IReadOnlyList<FactDefinition> Facts => _facts;

    public void Step(in StepContext context)
    {
        for (var i = 0; i < _facts.Length; i++)
        {
            var value = _values[i].Evaluate(_state);
            if (value.Kind == StateValueKind.None || (value.Kind == StateValueKind.Text && value.AsText().Length == 0)) continue;
            if (_knowledge.StateOf(_facts[i].Id) == KnowledgeState.Known &&
                _knowledge.TryGet(_knownValuePaths[i], out var known) && known.Equals(value))
                continue;
            _knowledge.ReportTelemetryReading(_facts[i], value, context.Now);
        }
    }
}
