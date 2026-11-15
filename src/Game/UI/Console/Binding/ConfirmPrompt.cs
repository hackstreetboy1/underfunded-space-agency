namespace Usa.Game.UI.Console.Binding;

public enum ConfirmPromptKind
{
    /// <summary>A guarded remote control on the systems board (UI.md 3.9, 5.2).</summary>
    Control,

    /// <summary>A high-impact crew or remote command from comms (UI.md 5.2, Gate 2 G2-09).</summary>
    Command,

    /// <summary>Stop on a crew task that is unsafe to interrupt; shown with the caution "UNSAFE TO STOP" flag (Gate 2 decision 5).</summary>
    StopTask,
}

/// <summary>What the high-impact confirmation dialog shows (UI.md 5.2).</summary>
/// <param name="Title">The action, e.g. <c>Arm RCS ring A</c>, or the task to stop.</param>
/// <param name="Effects">Consequences in words, e.g. the mission-rule warning.</param>
/// <param name="ExecutorName">Who carries it out (<c>Cal Brandt</c>, <c>Crew</c>, a remote label); null when not known.</param>
public sealed record ConfirmPrompt(
    ConfirmPromptKind Kind,
    string Title,
    IReadOnlyList<string> Effects,
    bool Irreversible = false,
    string? ExecutorName = null);
