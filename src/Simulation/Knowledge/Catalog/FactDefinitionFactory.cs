using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge.Catalog;

internal static class FactDefinitionFactory
{
    /// <summary>Builds a concrete definition; <paramref name="substitute"/> fills placeholders in label and source.</summary>
    /// <exception cref="ArgumentException">The id or source is not a valid id.</exception>
    public static FactDefinition Create(FactEntry entry, string id, Func<string, string> substitute)
    {
        var source = substitute(entry.Source);
        ComponentId? sourceComponent = entry.SourceKind == FactSourceKind.Crew ? null : new ComponentId(source);
        CrewId? sourceCrew = entry.SourceKind == FactSourceKind.Crew ? new CrewId(source) : null;
        var downlink = entry.Downlink is { } d
            ? new DownlinkDefinition(d.TelemetryPath, d.Bands ?? [], d.Words ?? [])
            : null;

        return new FactDefinition(
            new FactId(id),
            substitute(entry.Label),
            entry.Unit,
            entry.Kind,
            entry.Format,
            entry.Decimals,
            entry.ReadMode,
            entry.Methods,
            sourceComponent,
            sourceCrew,
            entry.SourceKind,
            entry.StaleAfterS,
            entry.ConflictTolerance,
            entry.ConflictGroup,
            entry.Task,
            downlink,
            entry.ConfirmsComponent ? sourceComponent : null,
            entry.NominalValues ?? [],
            entry.ValuePath is { } valuePath ? substitute(valuePath) : null);
    }
}
