using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

public sealed record CrewMemberSnapshot(CrewId Id, string Name, string Role)
{
    /// <summary>0-100; null when the ground does not know it.</summary>
    public double? HealthPercent { get; init; }

    /// <summary>Activity line, e.g. <c>Reading OAMS temps…</c>; null or empty reads <c>Idle</c>.</summary>
    public string? Activity { get; init; }

    /// <summary>Speech is playing (waveform indicator).</summary>
    public bool IsSpeaking { get; init; }

    /// <summary>MET of death; set once the astronaut has died.</summary>
    public MissionTime? DiedAt { get; init; }

    /// <summary>Show the fatigue icon (only when non-nominal).</summary>
    public bool Fatigued { get; init; }

    /// <summary>Show the hydration icon (only when non-nominal).</summary>
    public bool Dehydrated { get; init; }
}
