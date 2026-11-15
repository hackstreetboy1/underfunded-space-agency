using Usa.Game.Content;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Player-facing wording of the session adapters (event log, alarm lines, forecast sentences, crew activity, the diagram's
/// confirmation): <c>console.*</c> keys of <c>content/strings.json</c> through the host's <see cref="ITextSource"/>.
/// A key the source lacks (no source in tests, or a key not yet added to the file) falls back to the English defaults
/// below; a test keeps every default that the file defines identical to it.
/// </summary>
public sealed class AdapterText
{
    private readonly ITextSource? _source;

    public AdapterText(ITextSource? source) => _source = source;

    public static AdapterText Default { get; } = new(null);

    /// <summary>Default text by full key (<c>console.&lt;key&gt;</c>).</summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["console.logSourceGround"] = "Ground",
        ["console.logSourceAnnunciator"] = "Annunciator",
        ["console.logSourceFlightDirector"] = "Flight Director",
        ["console.logSourceMission"] = "Mission",
        ["console.logSourceTelemetry"] = "Telemetry",
        ["console.logRowAlarmCleared"] = "Alarm cleared: {alarm}",
        ["console.logRowAlarmsAcknowledged"] = "Alarms acknowledged ({count})",
        ["console.logRowCommand"] = "Command: {action}",
        ["console.logRowReadRequested"] = "Read requested: {action}",
        ["console.logRowRefused"] = "Refused: {action}",
        ["console.logRowTaskStarted"] = "Started: {task}",
        ["console.logRowTaskCompleted"] = "Completed: {task}",
        ["console.logRowTaskFailed"] = "Failed: {task}",
        ["console.logRowTaskStopped"] = "Stopped: {task}",
        ["console.logRowCrewBand"] = "{crew} is {band}",
        ["console.logRowCrewLost"] = "{crew} lost",
        ["console.logRowConflict"] = "Conflicting readings: {fact}",
        ["console.logRowConfirmedNominal"] = "{component} confirmed nominal",
        ["console.logRowConfirmedDamaged"] = "{component} confirmed {condition}",
        ["console.logRowConfirmedDamagedVariant"] = "{component} confirmed {condition} ({variant})",
        ["console.logRowConfirmedDamagedWord"] = "damaged",
        ["console.logRowTelemetry"] = "Telemetry: {fact} {band}",
        ["console.logRowPhase"] = "Phase: {phase}",
        ["console.logRowObjectiveComplete"] = "Objective complete: {objective}",
        ["console.logRowDocked"] = "Docked with {vehicle}",
        ["console.logRowUndocked"] = "Undocked from {vehicle}",
        ["console.logRowJettisoned"] = "Jettisoned: {section}",
        ["console.logRowChute"] = "{chute} chute: {result}",
        ["console.logRowTip"] = "New Flight Director tip",
        ["console.logRowMissionEnded"] = "Mission ended: {outcome}",
        ["console.chuteDrogue"] = "Drogue",
        ["console.chuteMain"] = "Main",
        ["console.chuteResultDeployed"] = "deployed",
        ["console.chuteResultAlreadyDeployed"] = "already out",
        ["console.chuteResultAlreadyFailed"] = "already failed",
        ["console.chuteResultTooHigh"] = "failed, too high",
        ["console.chuteResultTooFast"] = "failed, too fast",
        ["console.chuteResultLanded"] = "not deployed, landed",
        ["console.outcomeRecovered"] = "crew recovered",
        ["console.outcomeAllCrewLost"] = "all crew lost",
        ["console.alarmRateHigh"] = "Angular rate high",
        ["console.alarmBrownout"] = "{component} brownout",
        ["console.alarmDockingOverstress"] = "Docking adapter overstress",
        ["console.alarmCrewWorsening"] = "{crew}: condition worsening",
        ["console.alarmCrewUnknown"] = "Crew member",
        ["console.alarmTelemetryBand"] = "{fact}: {band} (telemetry)",
        ["console.alarmTankEmpty"] = "{component} empty",
        ["console.alarmCabinCo2"] = "Cabin CO2 high",
        ["console.alarmCabinO2"] = "Cabin O2 low",
        ["console.alarmCabinPressure"] = "Cabin pressure low",
        ["console.alarmCabinTemperature"] = "Cabin temperature out of limits",
        ["console.alarmOther"] = "Annunciator alarm",
        ["console.alarmSourceRateGyros"] = "Rate gyros",
        ["console.alarmSourceCabin"] = "Cabin sensors",
        ["console.crewOnTask"] = "On task: {task}",
        ["console.crewOnEva"] = "On EVA",
        ["console.forecastReasonNotChecked"] = "{resource} not checked",
        ["console.forecastReasonOld"] = "{resource} reading is old",
        ["console.forecastAssumeCrew"] = "Crew aboard: {count}",
        ["console.forecastAssumeFood"] = "Food ration at {percent} %",
        ["console.forecastAssumeWater"] = "Water ration at {percent} %",
        ["console.forecastAssumeQuantityStale"] = "{resource} reading is old",
        ["console.forecastAssumeQuantityUnknown"] = "{resource} not checked; launch figure used",
        ["console.forecastAssumeUseStale"] = "{resource} use rate estimated",
        ["console.forecastAssumeUseUnknown"] = "{resource} use rate unknown",
        ["console.forecastAssumeProductionStale"] = "{resource} production estimated",
        ["console.forecastAssumeProductionUnknown"] = "{resource} production unknown",
        ["console.forecastAssumePlanEnd"] = "Recovery in {hours} h",
        ["console.forecastAssumeBeyondHorizon"] = "Nothing runs out within {hours} h",
        ["console.confirmedBy"] = "{observer} · {method}",
        ["console.methodGlance"] = "glance",
        ["console.methodInstrument"] = "instrument read",
        ["console.methodInspection"] = "inspection",
        ["console.methodTest"] = "test",
        ["console.methodDownlink"] = "downlink",
        ["console.methodModel"] = "ground estimate",
        ["console.methodTelemetry"] = "telemetry dump",
        ["console.commandWithTarget"] = "{action} {target}",
    };

    /// <param name="key">Full key, e.g. <c>console.logRowPhase</c>.</param>
    /// <exception cref="KeyNotFoundException">Neither the source nor the defaults define the key.</exception>
    public string Get(string key) => Find(key) ?? throw new KeyNotFoundException($"No text for '{key}' in content/strings.json or the adapter defaults.");

    /// <summary>The source's text for <paramref name="key"/>, else the default, else null (optional keys).</summary>
    public string? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        switch (_source)
        {
            case StringTable table when table.TryGet(key, out var found):
                return found;
            case null or StringTable:
                break;
            default:
                try
                {
                    return _source.Get(key);
                }
                catch (KeyNotFoundException)
                {
                    break;
                }
        }
        return Defaults.GetValueOrDefault(key);
    }

    /// <summary>Readable words for a data id with no display text: <c>docked_ops</c> → <c>Docked ops</c>.</summary>
    public static string Words(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var words = id.Replace('_', ' ').Trim();
        return words.Length > 0 ? char.ToUpperInvariant(words[0]) + words[1..] : words;
    }

    /// <summary>The <c>console.&lt;prefix&gt;&lt;Pascal word&gt;</c> text for a data word (<c>failed</c> → <c>console.statusFailed</c>), or null.</summary>
    public string? FindWord(string prefix, string word)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(word);
        var pascal = new System.Text.StringBuilder(prefix.Length + word.Length + 8).Append("console.").Append(prefix);
        var upper = true;
        foreach (var c in word)
        {
            if (c is '_' or ' ' or '-')
            {
                upper = true;
                continue;
            }
            pascal.Append(upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            upper = false;
        }
        return Find(pascal.ToString());
    }

    public string Format(string key, params (string Name, string Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return TextTemplate.Fill(Get(key), name =>
        {
            foreach (var (candidate, value) in values)
            {
                if (string.Equals(candidate, name, StringComparison.Ordinal)) return value;
            }
            return null;
        });
    }
}
