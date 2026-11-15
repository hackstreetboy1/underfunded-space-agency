using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>A candidate observation from a producer (crew task, glance read, forecast) before validation.</summary>
public sealed record ObservationReport(
    FactId Fact,
    double? Value,
    string? Qualitative,
    CrewId? Observer,
    ObservationMethod Method,
    double Confidence,
    MissionTime Time,
    IReadOnlyList<ComponentId> Scope)
{
    /// <summary>The command that produced it (a glance read or crew task); copied onto the <see cref="Observation"/>.</summary>
    public CommandId? Origin { get; init; }

    /// <summary>A numeric reading by a crew member.</summary>
    public static ObservationReport Reading(FactId fact, double value, CrewId observer, ObservationMethod method, MissionTime time) =>
        new(fact, value, null, observer, method, 1.0, time, []);

    /// <summary>A text report by a crew member (status, condition) covering <paramref name="scope"/>.</summary>
    public static ObservationReport Report(FactId fact, string text, CrewId observer, ObservationMethod method, MissionTime time, params ComponentId[] scope) =>
        new(fact, null, text, observer, method, 1.0, time, scope);

    /// <summary>A model-derived value (forecast, ground model).</summary>
    public static ObservationReport Estimate(FactId fact, double value, double confidence, MissionTime time) =>
        new(fact, value, null, null, ObservationMethod.Model, confidence, time, []);

    /// <summary>A precise sensor reading from a ground telemetry dump (no observer): a number, or text for text facts.</summary>
    public static ObservationReport Telemetry(FactId fact, double? value, string? text, MissionTime time) =>
        new(fact, value, text, null, ObservationMethod.Telemetry, 1.0, time, []);

    internal Observation ToObservation(ObservationId id) =>
        new(id, Fact, Value, Qualitative, Observer, Method, Confidence, Time, Scope) { Origin = Origin };
}
