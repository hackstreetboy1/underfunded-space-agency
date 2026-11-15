using Usa.Game.Presentation.Console.Snapshots;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Player-facing line, systems-board tab and source for an alarm id (<see cref="AlarmMonitor"/>: <c>rate.&lt;vehicle&gt;</c>,
/// <c>cabin.*</c>, <c>power.brownout.&lt;bus&gt;</c>, <c>docking.overstress</c>, <c>crew.&lt;id&gt;</c>, <c>downlink.&lt;fact&gt;</c>,
/// <c>tank.&lt;component&gt;</c>). The alarm's own reading is never shown: an annunciator says that, not how much.
/// <para><c>AlarmCleared</c> carries no subject, so <see cref="Observe"/> remembers the subject of every raise;
/// <see cref="DescribeCleared"/> uses it (or the subject the id itself ends with).</para>
/// </summary>
public sealed class AlarmText
{
    private readonly Dictionary<string, string> _componentNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _subjects = new(StringComparer.Ordinal);
    private readonly FactCatalog _facts;
    private readonly CrewNames _crew;
    private readonly AdapterText _text;
    private readonly ConsoleLayout _layout;

    public AlarmText(MissionData data, CrewNames crew, AdapterText? text = null, ConsoleLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(crew);
        _facts = data.Facts;
        _crew = crew;
        _text = text ?? AdapterText.Default;
        _layout = layout ?? ConsoleLayout.MissionOne;
        foreach (var vehicle in data.Vehicles)
        {
            foreach (var component in vehicle.Components) _componentNames[component.Id.Value] = component.Name;
        }
    }

    /// <summary>Remembers the subject of a raised alarm for <see cref="DescribeCleared"/>.</summary>
    public void Observe(AlarmRaised raised)
    {
        ArgumentNullException.ThrowIfNull(raised);
        if (raised.Subject.Length > 0) _subjects[raised.AlarmId] = raised.Subject;
    }

    public AlarmSnapshot Snapshot(ActiveAlarm alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        var (text, tab, source) = Describe(alarm.AlarmId, alarm.Subject);
        return new AlarmSnapshot(alarm.AlarmId, AdapterMaps.Priority(alarm.Level), tab, text, alarm.RaisedAt)
        {
            Acknowledged = alarm.Acknowledged,
            Source = source,
        };
    }

    /// <summary>The line of a cleared alarm; forgets its remembered subject.</summary>
    public (string Text, SystemsTab Tab, string Source) DescribeCleared(string alarmId)
    {
        ArgumentNullException.ThrowIfNull(alarmId);
        var subject = _subjects.Remove(alarmId, out var remembered) ? remembered : SubjectInId(alarmId);
        return Describe(alarmId, subject);
    }

    public (string Text, SystemsTab Tab, string Source) Describe(string alarmId, string subject)
    {
        ArgumentNullException.ThrowIfNull(alarmId);
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Length == 0) subject = SubjectInId(alarmId);
        if (alarmId.StartsWith("rate.", StringComparison.Ordinal))
            return (_text.Get("console.alarmRateHigh"), SystemsTab.PropAttitude, _text.Get("console.alarmSourceRateGyros"));
        if (alarmId.StartsWith("power.brownout.", StringComparison.Ordinal))
            return (_text.Format("console.alarmBrownout", ("component", Component(subject))), SystemsTab.Power, Component(subject));
        if (alarmId == "docking.overstress")
            return (_text.Get("console.alarmDockingOverstress"), SystemsTab.StructureLanding, Component(subject));
        if (alarmId.StartsWith("crew.", StringComparison.Ordinal))
        {
            var known = subject.Length > 0;
            var name = known ? _crew.FullName(new CrewId(subject)) : _text.Get("console.alarmCrewUnknown");
            return (_text.Format("console.alarmCrewWorsening", ("crew", name)), SystemsTab.LifeSupport,
                known ? _crew.ShortName(new CrewId(subject)) : _text.Get("console.logSourceAnnunciator"));
        }
        if (alarmId.StartsWith("downlink.", StringComparison.Ordinal))
        {
            var line = _text.Format("console.alarmTelemetryBand", ("fact", FactLabel(subject)), ("band", AlarmBand(subject)));
            return (line, TabOfFact(subject), _text.Get("console.logSourceTelemetry"));
        }
        if (alarmId.StartsWith("tank.", StringComparison.Ordinal))
        {
            var tab = subject.Length > 0 ? ConsoleLayout.TabOf(new ComponentId(subject), null) : SystemsTab.PropAttitude;
            return (_text.Format("console.alarmTankEmpty", ("component", Component(subject))), tab, Component(subject));
        }
        var cabin = _text.Get("console.alarmSourceCabin");
        return alarmId switch
        {
            "cabin.ppco2" => (_text.Get("console.alarmCabinCo2"), SystemsTab.LifeSupport, cabin),
            "cabin.ppo2" => (_text.Get("console.alarmCabinO2"), SystemsTab.LifeSupport, cabin),
            "cabin.pressure" => (_text.Get("console.alarmCabinPressure"), SystemsTab.LifeSupport, cabin),
            "cabin.temp" => (_text.Get("console.alarmCabinTemperature"), SystemsTab.Thermal, cabin),
            _ => (_text.Get("console.alarmOther"), SystemsTab.Power, _text.Get("console.logSourceAnnunciator")),
        };
    }

    /// <summary><c>crew.june_okafor</c> → <c>june_okafor</c>, <c>power.brownout.tandem/power/bus_a</c> → the bus; empty for fixed ids.</summary>
    private static string SubjectInId(string alarmId)
    {
        foreach (var prefix in SubjectPrefixes)
        {
            if (alarmId.StartsWith(prefix, StringComparison.Ordinal)) return alarmId[prefix.Length..];
        }
        return string.Empty;
    }

    private static readonly string[] SubjectPrefixes = ["power.brownout.", "crew.", "downlink.", "tank."];

    private string Component(string id) => _componentNames.TryGetValue(id, out var name) ? name : AdapterText.Words(id);

    private string FactLabel(string id) =>
        id.Length > 0 && _facts.TryResolve(new FactId(id), out var fact) ? fact.Label : AdapterText.Words(id);

    /// <summary>
    /// The band the downlink alarm sits in (<see cref="AlarmMonitor"/>): the lowest band, or the highest when the lowest is
    /// the nominal one (docking load OVERLOAD).
    /// </summary>
    private string AlarmBand(string factId)
    {
        if (factId.Length == 0 || !_facts.TryResolve(new FactId(factId), out var fact) || fact.Downlink is not { Bands.Count: > 0 } downlink)
            return string.Empty;
        var lowest = downlink.Bands[0].Word;
        var word = string.Equals(lowest, "NOMINAL", StringComparison.OrdinalIgnoreCase) ? downlink.Bands[^1].Word : lowest;
        return word.ToUpperInvariant();
    }

    private SystemsTab TabOfFact(string factId) =>
        factId.Length > 0 && _layout.GroupOf(new FactId(factId)) is { } group ? group.Tab : SystemsTab.Power;
}
