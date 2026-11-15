using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Turns what the knowledge base holds about a fact into a <see cref="ReadoutSnapshot"/>: the latest crew measurement with
/// its observer and MET, <c>STALE</c> once older than the fact's <c>staleAfterS</c>, the disagreeing value of a conflict,
/// the model estimate, or the latest ground band. Trends come from the fact's own observation history (the knowledge
/// journal). It never reads simulation truth, so an unobserved fact is always <see cref="ReadoutKnowledge.Unknown"/>.
/// </summary>
public sealed class KnowledgeReadouts
{
    /// <summary>Two measurements closer than this give no trend (reading noise would dominate).</summary>
    public const long MinTrendSpanMs = 20_000;

    public const string TelemetrySource = "Telemetry";
    /// <summary>String key of a model estimate's source (<c>ground estimate</c>).</summary>
    public const string ModelSourceKey = "console.methodModel";

    private readonly FactCatalog _catalog;
    private readonly KnowledgeBase _knowledge;
    private readonly CrewNames _names;
    private readonly string _modelSource;

    /// <param name="text">Source words (<c>console.methodModel</c>); null uses the adapters' English defaults.</param>
    public KnowledgeReadouts(FactCatalog catalog, KnowledgeBase knowledge, CrewNames names, AdapterText? text = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(names);
        _catalog = catalog;
        _knowledge = knowledge;
        _names = names;
        _modelSource = (text ?? AdapterText.Default).Get(ModelSourceKey);
    }

    public ReadoutSnapshot Read(FactId id, MissionTime now)
    {
        if (!_catalog.TryResolve(id, out var fact)) return ReadoutSnapshot.UnknownIn(string.Empty);
        var known = _knowledge.Describe(id);
        switch (known.State)
        {
            case KnowledgeState.Known or KnowledgeState.Stale or KnowledgeState.Conflicting when known.Latest is { } latest:
            {
                var stale = known.State == KnowledgeState.Stale || fact.IsStale(latest.Time, now);
                var conflict = known.State == KnowledgeState.Conflicting && known.ConflictsWith.Count > 0;
                var state = conflict ? ReadoutKnowledge.Conflict : stale ? ReadoutKnowledge.Stale : ReadoutKnowledge.Known;
                return new ReadoutSnapshot(state, fact.Unit)
                {
                    Value = latest.Value,
                    Source = SourceOf(latest),
                    ObservedAt = latest.Time,
                    StaleAge = stale ? TimeSpan.FromMilliseconds(Math.Max(0, now.Milliseconds - latest.Time.Milliseconds)) : null,
                    TrendPerHour = TrendPerHour(id),
                    ConflictValue = conflict ? known.ConflictsWith[0].Value : null,
                    GroundBand = known.GroundBandWord,
                };
            }
            case KnowledgeState.Estimated when known.Estimate is { } estimate:
                return new ReadoutSnapshot(ReadoutKnowledge.Estimated, fact.Unit)
                {
                    Value = estimate.Value,
                    Source = _modelSource,
                    ObservedAt = estimate.Time,
                    GroundBand = known.GroundBandWord,
                };
            default:
                return known.GroundBand is { } band
                    ? new ReadoutSnapshot(ReadoutKnowledge.GroundBand, fact.Unit)
                    {
                        GroundBand = band.Qualitative,
                        Source = TelemetrySource,
                        ObservedAt = band.Time,
                    }
                    : ReadoutSnapshot.UnknownIn(fact.Unit);
        }
    }

    /// <summary>The latest crew measurement (fresh or stale), or null when there is none.</summary>
    public Observation? LatestMeasurement(FactId id) => _knowledge.Describe(id).Latest;

    /// <summary>Slope between the latest measurement and the newest one at least <see cref="MinTrendSpanMs"/> before it, per hour.</summary>
    public double? TrendPerHour(FactId id)
    {
        var history = _knowledge.HistoryOf(id);
        Observation? latest = null;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var observation = history[i];
            if (!IsMeasurement(observation)) continue;
            if (latest is null)
            {
                latest = observation;
                continue;
            }
            var spanMs = latest.Time.Milliseconds - observation.Time.Milliseconds;
            if (spanMs < MinTrendSpanMs) continue;
            return (latest.Value!.Value - observation.Value!.Value) / (spanMs / 3_600_000.0);
        }
        return null;
    }

    public string SourceOf(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Observer is { } crew) return _names.ShortName(crew);
        return observation.Method switch
        {
            ObservationMethod.Downlink => TelemetrySource,
            ObservationMethod.Model => _modelSource,
            _ => TelemetrySource,
        };
    }

    private static bool IsMeasurement(Observation observation) =>
        observation.Value is not null && observation.Method is not (ObservationMethod.Downlink or ObservationMethod.Model);
}
