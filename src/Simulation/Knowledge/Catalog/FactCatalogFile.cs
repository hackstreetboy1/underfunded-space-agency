using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge.Catalog;

/// <summary>Root of <c>catalog/facts.json</c> (schema <c>facts/1</c>).</summary>
public sealed record FactCatalogFile(
    string Schema,
    FactPhrases Phrases,
    IReadOnlyList<MethodCapabilities> Methods,
    IReadOnlyList<FactEntry> Facts) : IVersionedDefinition;

/// <summary>Fixed narration phrases. <c>StaleSuffix</c> contains <c>{met}</c> (HH:MM).</summary>
public sealed record FactPhrases(string Unobserved, string StaleSuffix);

/// <summary>What an observation method can establish.</summary>
/// <param name="RequiresObserver">True: a living crew member must make it. False: it must have no observer.</param>
public sealed record MethodCapabilities(
    ObservationMethod Method,
    bool RequiresObserver,
    bool CanConfirmComponent,
    ObservationYield Yield);

/// <summary>One fact, or a fact pattern when <see cref="Matcher"/> is set (placeholders <c>{nn}</c>, <c>{n}</c>, <c>{component}</c>, <c>{crew}</c>).</summary>
/// <param name="Methods">Observation methods that can establish the fact (downlink is implied by <see cref="Downlink"/>).</param>
/// <param name="Source">The component (or crew member) the value comes from; may use the pattern's placeholder.</param>
/// <param name="ConflictGroup">Facts in one group measure the same quantity; fresh values that differ conflict.</param>
/// <param name="Task">The crew task that produces the fact when <see cref="ReadMode"/> is task.</param>
/// <param name="ConfirmsComponent">The fact is a component's condition and confirms it (component patterns only).</param>
/// <param name="NominalValues">Text values that mean "healthy".</param>
/// <param name="ValuePath">
/// Optional expression the true (sensor) value is read from, usually one state path such as <c>@cabin.tandem.lioh_pct</c>
/// or a conversion such as <c>@vehicle.tandem.power_load_w / 1000</c>; placeholders are substituted as in <see cref="Source"/>.
/// Null: the integrator derives the value from <see cref="Source"/>.
/// </param>
public sealed record FactEntry(
    string Id,
    string Label,
    string Unit,
    FactValueKind Kind,
    FactFormat Format,
    FactReadMode ReadMode,
    IReadOnlyList<ObservationMethod> Methods,
    string Source,
    FactSourceKind SourceKind,
    double StaleAfterS,
    int Decimals = 1,
    double ConflictTolerance = 0,
    string? ConflictGroup = null,
    TaskId? Task = null,
    DownlinkEntry? Downlink = null,
    FactMatcherEntry? Matcher = null,
    bool ConfirmsComponent = false,
    IReadOnlyList<string>? NominalValues = null,
    string? ValuePath = null);

/// <param name="From">First index (index matcher only).</param>
/// <param name="To">Last index, inclusive (index matcher only).</param>
public sealed record FactMatcherEntry(FactMatcherKind Kind, int From = 0, int To = 0);

/// <summary>Ground telemetry for a fact: numeric <see cref="Bands"/> or text <see cref="Words"/>.</summary>
/// <param name="TelemetryPath">State path the downlink samples (served by the owning area).</param>
public sealed record DownlinkEntry(
    string TelemetryPath,
    IReadOnlyList<GroundBand>? Bands = null,
    IReadOnlyList<GroundWord>? Words = null);

/// <summary>A coarse band; applies from <see cref="From"/> (inclusive) up to the next band. The first band has no lower bound.</summary>
public sealed record GroundBand(string Word, double? From = null);

/// <summary>Maps a telemetry text value to its ground word.</summary>
public sealed record GroundWord(string Value, string Word);
