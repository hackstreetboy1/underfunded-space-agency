using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Alarms;

/// <summary>One alarm as listed (annunciator list, systems tab, pinned rows of the event log).</summary>
/// <param name="MetText">Raise time, <c>06:58:12</c>.</param>
/// <param name="Acknowledged">Acknowledged by the sim or locally, pending the sim's confirmation.</param>
/// <param name="Pinned">Unacknowledged warning or master alarm, held at the top until acknowledged.</param>
public sealed record AlarmRow(
    string AlarmId,
    AlarmPriority Priority,
    SystemsTab System,
    string Text,
    string? Source,
    MissionTime RaisedAt,
    string MetText,
    bool Acknowledged,
    bool Pinned,
    StatusVisual Visual);
