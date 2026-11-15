using Usa.Simulation.Data;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>
/// Root of <c>missions/&lt;id&gt;/session.json</c> (schema <c>mission_session/1</c>): what the session layer does for this
/// mission that no area file describes. Every member is optional; a mission without the file gets <see cref="Empty"/>.
/// </summary>
/// <param name="Burns">Engines the session fires on command, served as <c>session.burn.&lt;id&gt;.*</c>.</param>
/// <param name="EntryChecklist">Tasks whose success effects the armed entry checklist applies at the nominal chute altitudes.</param>
/// <param name="DockingTasks">Crew tasks that count as docking activity for time compression.</param>
/// <param name="Record">Mission rules the scoring record reads.</param>
/// <param name="Report">What the headless scenario player samples and reports (balancing only; never affects the run).</param>
/// <param name="Pacing">What the host's time compression treats as the calm opening and as decisive moments.</param>
/// <param name="Sun">Overrides <c>flight.json</c> <c>sun</c> (<see cref="SunModel"/>); null keeps the flight Sun, if any (mission 1 has neither).</param>
/// <param name="Telemetry">Instruments downlinked as precise telemetry readings (<see cref="Knowledge.TelemetryReadingSampler"/>).</param>
public sealed record MissionSessionConfig(
    string Schema,
    IReadOnlyList<BurnEngineDefinition>? Burns = null,
    EntryChecklistDefinition? EntryChecklist = null,
    IReadOnlyList<TaskId>? DockingTasks = null,
    RecordRulesDefinition? Record = null,
    ScenarioReportDefinition? Report = null,
    PacingDefinition? Pacing = null,
    SunDefinition? Sun = null,
    TelemetryDefinition? Telemetry = null) : IVersionedDefinition
{
    public const string SchemaTag = "mission_session/1";
    public const string FileName = "session.json";

    public static MissionSessionConfig Empty { get; } = new(SchemaTag);

    public IReadOnlyList<BurnEngineDefinition> BurnEngines => Burns ?? [];

    public static string PathFor(string missionId) => $"{MissionData.MissionDirectory(missionId)}/{FileName}";

    /// <summary>Loads the mission's file, or <see cref="Empty"/> when it has none.</summary>
    /// <exception cref="DefinitionException">Every problem in the file.</exception>
    public static MissionSessionConfig Load(DefinitionLoader loader, string missionId, IReadOnlyCollection<string> missionVehicles)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(missionVehicles);
        var path = PathFor(missionId);
        if (!loader.Source.Exists(path)) return Empty;
        var config = loader.Load<MissionSessionConfig>(path, SchemaTag);
        var errors = new DefinitionErrors();
        config.Validate(path, missionVehicles, errors);
        errors.ThrowIfAny();
        return config;
    }

    private void Validate(string file, IReadOnlyCollection<string> missionVehicles, DefinitionErrors errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var burns = BurnEngines;
        for (var i = 0; i < burns.Count; i++)
        {
            var burn = burns[i];
            var path = $"$.burns[{i}]";
            if (!IsSnakeCase(burn.Id)) errors.Add(file, path + ".id", $"'{burn.Id}' is not a snake_case id.");
            else if (!ids.Add(burn.Id)) errors.Add(file, path + ".id", $"Burn '{burn.Id}' is listed twice.");
            if (!missionVehicles.Contains(burn.Vehicle)) errors.Add(file, path + ".vehicle", $"'{burn.Vehicle}' is not a vehicle of this mission.");
            if (burn.Kind == BurnEngineKind.DockedMainEngine && burn.Thrusters is { Count: > 0 })
                errors.Add(file, path + ".thrusters", "A docked main engine fires one engine; name it with 'engine'.");
            if (burn.Kind == BurnEngineKind.Translation && burn.Engine is not null)
                errors.Add(file, path + ".engine", "A translation burn fires thrusters; list them with 'thrusters'.");
        }
        ValidateSunAndTelemetry(file, errors);
        if (Report is { } report)
        {
            var labels = new HashSet<string>(StringComparer.Ordinal);
            var samples = report.Samples ?? [];
            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                if (string.IsNullOrWhiteSpace(sample.Label)) errors.Add(file, $"$.report.samples[{i}].label", "Label is empty.");
                else if (!labels.Add(sample.Label)) errors.Add(file, $"$.report.samples[{i}].label", $"Label '{sample.Label}' is listed twice.");
                if (string.IsNullOrWhiteSpace(sample.Path)) errors.Add(file, $"$.report.samples[{i}].path", "Path is empty.");
            }
        }
    }

    private void ValidateSunAndTelemetry(string file, DefinitionErrors errors)
    {
        Sun?.Validate(file, "$.sun", errors);
        if (Telemetry is not { } telemetry) return;
        if (string.IsNullOrWhiteSpace(telemetry.Active)) errors.Add(file, "$.telemetry.active", "An activity condition is required (e.g. the entry phase).");
        var facts = new HashSet<FactId>();
        for (var i = 0; i < telemetry.Channels.Count; i++)
        {
            var channel = telemetry.Channels[i];
            if (!facts.Add(channel.Fact)) errors.Add(file, $"$.telemetry.channels[{i}].fact", $"Fact '{channel.Fact}' is listed twice.");
            if (string.IsNullOrWhiteSpace(channel.Path)) errors.Add(file, $"$.telemetry.channels[{i}].path", "Path is empty.");
            if (channel.ValidMax is { } max && !double.IsFinite(max)) errors.Add(file, $"$.telemetry.channels[{i}].validMax", "Must be finite.");
        }
    }

    private static bool IsSnakeCase(string id)
    {
        if (string.IsNullOrEmpty(id) || !char.IsAsciiLetterLower(id[0])) return false;
        foreach (var c in id)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_') return false;
        }
        return true;
    }
}

/// <summary>How the session fires a commanded engine.</summary>
public enum BurnEngineKind
{
    /// <summary>
    /// Undocked translation: the vehicle's thrusters fire at full valve for the staged duration along the staged inertial
    /// direction (<c>direction_deg</c>, <c>duration_s</c>, <c>start</c>), the vehicle holding attitude on it.
    /// </summary>
    Translation,

    /// <summary>
    /// Docked orbit change: the vehicle's main engine fires prograde for <c>duration_s</c> while docked, after the stack turns
    /// it prograde; prograde Δv added is served as <c>docked_dv_mps</c>.
    /// </summary>
    DockedMainEngine,
}

/// <summary>One commanded engine; its paths are <c>session.burn.&lt;Id&gt;.*</c>.</summary>
/// <param name="Vehicle">The vehicle carrying the engine or thrusters.</param>
/// <param name="Thrusters">Translation only: the thrusters that fire; default every ungrouped thruster pushing along the vehicle's +X axis.</param>
/// <param name="Engine">Docked main engine only: the engine component; default the vehicle's first engine.</param>
public sealed record BurnEngineDefinition(
    string Id,
    BurnEngineKind Kind,
    string Vehicle,
    IReadOnlyList<ComponentId>? Thrusters = null,
    ComponentId? Engine = null);

/// <summary>The deploy tasks the entry checklist runs automatically (their success effects, exactly as if the crew ran them).</summary>
public sealed record EntryChecklistDefinition(TaskId? DrogueTask = null, TaskId? MainTask = null);

/// <param name="DockingDamageRule">
/// Feasibility rule id that judges docking damage: when it fires with cause <c>scenario</c>, docking damage is not held against the crew.
/// </param>
/// <param name="StagingBeforeRetrofireEvent">
/// Mission event of the staging the return expects before retrofire; null when the mission has none (always satisfied).
/// </param>
public sealed record RecordRulesDefinition(string? DockingDamageRule = null, string? StagingBeforeRetrofireEvent = null);

/// <param name="IncidentEvent">Mission event the balance metrics measure the spin from.</param>
/// <param name="Samples">Extra values sampled every MET second, printed by the scenario runner.</param>
public sealed record ScenarioReportDefinition(string? IncidentEvent = null, IReadOnlyList<ScenarioSampleDefinition>? Samples = null);

/// <param name="CalmOpening">The mission's calm opening (GDD 12.4), which caps the compression rate; null when it has none.</param>
/// <param name="DecisiveEvents">
/// Mission events that are decisive moments: the host holds real time for a short window after each fires (docking, retrofire,
/// entry interface, chutes, splashdown). Flight's own predictions (scheduled burn, entry interface, chutes, splashdown) need no listing.
/// </param>
public sealed record PacingDefinition(CalmOpeningDefinition? CalmOpening = null, IReadOnlyList<string>? DecisiveEvents = null);

/// <param name="Phases">Mission phases the opening spans.</param>
/// <param name="UntilEvent">Mission event that ends the opening early (e.g. the first incident); null when only the phases decide.</param>
public sealed record CalmOpeningDefinition(IReadOnlyList<string> Phases, string? UntilEvent = null);

/// <param name="Label">Short name in reports, e.g. <c>OAMS</c>.</param>
/// <param name="Path">State path (without '@').</param>
/// <param name="Unit">Unit printed after the value.</param>
public sealed record ScenarioSampleDefinition(string Label, string Path, string Unit = "");

/// <param name="Active">Expression; readings flow only while it is true (e.g. <c>@mission.phase == 'entry'</c>).</param>
public sealed record TelemetryDefinition(string Active, IReadOnlyList<TelemetryChannelDefinition> Channels);

/// <param name="Fact">A number fact that allows the telemetry method.</param>
/// <param name="Path">The instrument's channel, <c>&lt;sensor component&gt;.&lt;channel field&gt;</c>: read through the sensor model.</param>
/// <param name="ValidMax">Readings above this are outside the instrument's range and are not reported.</param>
public sealed record TelemetryChannelDefinition(FactId Fact, string Path, double? ValidMax = null);
