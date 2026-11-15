using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <param name="AlarmId">Stable id, e.g. <c>rate.tandem</c>.</param>
/// <param name="System">Systems-board tab the alarm belongs to (and is counted under).</param>
/// <param name="Text">Player-facing line, e.g. <c>Roll rate high</c>.</param>
/// <param name="RaisedAt">When it was raised or last changed level; a re-raise needs a new acknowledgement.</param>
public sealed record AlarmSnapshot(string AlarmId, AlarmPriority Priority, SystemsTab System, string Text, MissionTime RaisedAt)
{
    public bool Acknowledged { get; init; }

    /// <summary>What raised it, e.g. a component or crew name.</summary>
    public string? Source { get; init; }
}
