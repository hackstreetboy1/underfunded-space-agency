using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>The request passed validation and is about to execute.</summary>
public sealed record CommandAccepted(MissionTime Time, CommandId Command, ActionId Action, CrewId? Assignee) : ISimEvent
{
    /// <inheritdoc cref="CommandExecuted.Arguments"/>
    public IReadOnlyDictionary<string, CommandArg> Arguments { get; init; } = CommandRequest.NoArgs;
}

/// <summary>The request was refused (including <see cref="ValidationStatus.CrewBusy"/>); the crew explains <paramref name="ReasonCode"/>.</summary>
public sealed record CommandRefused(MissionTime Time, CommandId Command, ActionId Action, ValidationStatus Status, string ReasonCode, string? Detail)
    : ISimEvent;

/// <summary>A high-impact or irreversible request waits for the player to confirm by resubmitting it with Confirmed set.</summary>
public sealed record CommandNeedsConfirmation(MissionTime Time, CommandId Command, ActionId Action, ImpactSummary Impact) : ISimEvent;

/// <summary>The task started, the remote control applied, the glance read was scheduled or the meta command ran.</summary>
public sealed record CommandExecuted(MissionTime Time, CommandId Command, ActionId Action, ActionKind Kind, CrewId? Assignee) : ISimEvent
{
    /// <summary>
    /// The arguments as validated against the action's parameter schema, in ordinal name order: typed, enum values normalised
    /// to their declared spelling. Empty for an action without parameters.
    /// </summary>
    public IReadOnlyDictionary<string, CommandArg> Arguments { get; init; } = CommandRequest.NoArgs;
}
