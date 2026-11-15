using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;
using CatalogImpact = Usa.Simulation.Commands.Catalog.ActionImpact;

namespace Usa.Game.Comms;

/// <summary>
/// What the confirmation dialog shows before a high-impact or irreversible action (UI.md 5.2). Confirming submits
/// <see cref="Command"/> once more with <c>Confirmed</c> set and the same command id. It auto-cancels after
/// <see cref="CommsOrchestrator.ConfirmationTimeout"/> without input, not counting freezes.
/// </summary>
/// <param name="Id">The command id.</param>
/// <param name="Title">Action title, e.g. "Arm RCS ring A or B".</param>
/// <param name="Arguments">The action's arguments as shown to the player, e.g. <c>ring = a</c>.</param>
/// <param name="ExecutorName">Who carries it out: a crew member, "Crew", or the remote-control label.</param>
public sealed record ConfirmationRequest(
    string Id,
    CommandRequest Command,
    string Title,
    IReadOnlyList<KeyValuePair<string, string>> Arguments,
    ImpactSummary Impact,
    CrewId? Executor,
    string ExecutorName)
{
    public bool Irreversible => Impact.Impact == CatalogImpact.Irreversible;

    /// <summary>Localisation keys of the consequences, in data order.</summary>
    public IReadOnlyList<string> EffectTextKeys => Impact.EffectTextKeys;

    /// <summary>Components the action touches; the UI groups them by system.</summary>
    public IReadOnlyList<ComponentId> AffectedComponents => Impact.AffectedComponents;
}

public enum ConfirmationOutcome
{
    Confirmed,
    Cancelled,

    /// <summary>No input for <see cref="CommsOrchestrator.ConfirmationTimeout"/>.</summary>
    Expired,

    /// <summary>A newer confirmation replaced it.</summary>
    Superseded,
}

public sealed record ConfirmationClosed(ConfirmationRequest Request, ConfirmationOutcome Outcome);
