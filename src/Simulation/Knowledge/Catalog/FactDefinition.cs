using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge.Catalog;

/// <summary>A concrete fact resolved from the catalog (patterns already substituted).</summary>
/// <param name="SourceComponent">The component the value is read from; null for crew self-reports.</param>
/// <param name="SourceCrew">The crew member a self-report is about; null otherwise.</param>
/// <param name="ConfirmsComponent">Set for component condition facts: the component this fact confirms.</param>
/// <param name="ValuePath">Expression source the value is read from (placeholders substituted), or null to derive it from the source.</param>
public sealed record FactDefinition(
    FactId Id,
    string Label,
    string Unit,
    FactValueKind Kind,
    FactFormat Format,
    int Decimals,
    FactReadMode ReadMode,
    IReadOnlyList<ObservationMethod> Methods,
    ComponentId? SourceComponent,
    CrewId? SourceCrew,
    FactSourceKind SourceKind,
    double StaleAfterS,
    double ConflictTolerance,
    string? ConflictGroup,
    TaskId? Task,
    DownlinkDefinition? Downlink,
    ComponentId? ConfirmsComponent,
    IReadOnlyList<string> NominalValues,
    string? ValuePath = null)
{
    public long StaleAfterMs { get; } = (long)Math.Round(StaleAfterS * 1000.0);

    /// <summary>True when <paramref name="method"/> can establish this fact (downlink only through telemetry).</summary>
    public bool Allows(ObservationMethod method)
    {
        if (method == ObservationMethod.Downlink) return Downlink is not null;
        for (var i = 0; i < Methods.Count; i++)
        {
            if (Methods[i] == method) return true;
        }
        return false;
    }

    /// <summary>Older than <see cref="StaleAfterS"/> at <paramref name="now"/>.</summary>
    public bool IsStale(MissionTime observedAt, MissionTime now) => now.Milliseconds - observedAt.Milliseconds > StaleAfterMs;

    public bool IsNominalText(string? text)
    {
        if (text is null) return false;
        for (var i = 0; i < NominalValues.Count; i++)
        {
            if (string.Equals(NominalValues[i], text, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
