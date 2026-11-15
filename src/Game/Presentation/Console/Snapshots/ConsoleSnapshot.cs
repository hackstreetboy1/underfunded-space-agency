using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>
/// Everything the mission console shows, already filtered to player knowledge. The session adapter builds one
/// from simulation state and events and hands it to <c>MissionConsoleViewModel.Apply</c>; view models never
/// query the simulation themselves (UI.md 6.4).
/// </summary>
public sealed record ConsoleSnapshot
{
    public required MissionTime Met { get; init; }

    public string MissionTitle { get; init; } = string.Empty;

    public string VehicleName { get; init; } = string.Empty;

    public MissionPhase Phase { get; init; }

    public CompressionSnapshot Compression { get; init; } = CompressionSnapshot.RealTime;

    public LinkState Link { get; init; }

    public IReadOnlyList<ObjectiveSnapshot> Objectives { get; init; } = [];

    /// <summary>Resource-strip tiles in display order.</summary>
    public IReadOnlyList<ResourceSnapshot> Resources { get; init; } = [];

    /// <summary>Null until the first forecast is computed.</summary>
    public ForecastSnapshot? Forecast { get; init; }

    /// <summary>Crew cards in display order.</summary>
    public IReadOnlyList<CrewMemberSnapshot> Crew { get; init; } = [];

    public ActiveTaskSnapshot? ActiveTask { get; init; }

    /// <summary>Active alarms; cleared alarms are simply absent.</summary>
    public IReadOnlyList<AlarmSnapshot> Alarms { get; init; } = [];

    public GuidanceTipSnapshot? Tip { get; init; }

    /// <summary>No new tip before this MET.</summary>
    public MissionTime? TipCooldownUntil { get; init; }

    public IReadOnlyList<SystemsGroupSnapshot> SystemsBoard { get; init; } = [];

    /// <summary>Log lines produced since the previous snapshot; appended once, never replayed.</summary>
    public IReadOnlyList<LogEntry> NewLogEntries { get; init; } = [];
}
