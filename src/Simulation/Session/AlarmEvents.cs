using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>Annunciator levels, mildest first. Master alarms need the crew's attention now (time compression drops to 1x).</summary>
public enum AlarmLevel
{
    Caution,
    Warning,
    Master,
}

/// <summary>An alarm became active, or its level changed (a raised alarm is unacknowledged).</summary>
/// <param name="AlarmId">Stable id, e.g. <c>rate.tandem</c>, <c>cabin.ppco2</c>, <c>power.brownout.tandem/power/bus_a</c>.</param>
/// <param name="Subject">What it is about (component, crew member, fact or vehicle id).</param>
/// <param name="Value">The reading that raised it, when numeric.</param>
public sealed record AlarmRaised(MissionTime Time, string AlarmId, AlarmLevel Level, string Subject, double? Value) : ISimEvent;

public sealed record AlarmCleared(MissionTime Time, string AlarmId) : ISimEvent;

/// <summary>The crew acknowledged every active alarm (meta command <c>acknowledge_alarms</c>).</summary>
public sealed record AlarmsAcknowledged(MissionTime Time, int Count) : ISimEvent;

/// <summary>An active alarm as the annunciator panel shows it.</summary>
public sealed record ActiveAlarm(string AlarmId, AlarmLevel Level, string Subject, double? Value, MissionTime RaisedAt, bool Acknowledged);
