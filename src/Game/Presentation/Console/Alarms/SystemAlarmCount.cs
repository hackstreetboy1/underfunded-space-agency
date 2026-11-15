using Usa.Game.Presentation.Console.Snapshots;

namespace Usa.Game.Presentation.Console.Alarms;

/// <summary>Alarm counts for one systems-board tab badge.</summary>
/// <param name="Highest">Highest active priority; null when the system has no active alarm.</param>
public readonly record struct SystemAlarmCount(SystemsTab System, int Active, int Unacknowledged, AlarmPriority? Highest);
