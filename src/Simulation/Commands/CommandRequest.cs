using System.Collections.ObjectModel;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

public enum CommandSource
{
    Voice,
    Text,
    QuickCommand,
    RemoteControl,
    Workspace,
    Replay,
}

/// <summary>The single input path into the simulation: every UI, AI and replay action is one of these.</summary>
/// <param name="Id">Deterministic "&lt;session&gt;-&lt;sequence&gt;"; also the AI request id. Retries reuse it.</param>
/// <param name="Assignee">A crew member, <see cref="AnyCrew"/> or null; the latter two pick the best available member.</param>
/// <param name="Confirmed">The player accepted the confirmation prompt for a high-impact action.</param>
public sealed record CommandRequest(
    CommandId Id,
    CommandSource Source,
    ActionId Action,
    CrewId? Assignee,
    IReadOnlyDictionary<string, CommandArg> Args,
    bool Confirmed)
{
    /// <summary>"crew": let the simulation pick the best available member.</summary>
    public static CrewId AnyCrew { get; } = new("crew");

    public static IReadOnlyDictionary<string, CommandArg> NoArgs => ReadOnlyDictionary<string, CommandArg>.Empty;
}
