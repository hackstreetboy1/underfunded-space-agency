using Usa.Simulation.Commands;

namespace Usa.Simulation.Guidance;

/// <summary>
/// The tutorial rails of the guided first run on Easy: while a numbered walkthrough step (<see cref="TutorialRailData"/>) is
/// pending, only that step's action is possible. The one gate every input path consults: the console greys out every control
/// <see cref="IsAllowed"/> refuses (tooltip <see cref="BlockedTooltip"/>), comms answers an off-script order with
/// <see cref="RedirectLine"/> instead of executing it, and the console bridge refuses any request <see cref="Allows"/> refuses.
/// Outside the railed walkthrough (Normal, an unguided run, after the last step) everything is allowed.
/// </summary>
public interface ITutorialRails
{
    /// <summary>A railed step is pending, done and being worked on by the game, or waiting for a retry.</summary>
    bool IsEngaged { get; }

    /// <summary>The current step; null while the rails are not engaged.</summary>
    TutorialRailsStep? Step { get; }

    /// <summary>The step or its phase changed.</summary>
    event Action? Changed;

    /// <summary>Whether a console control (<see cref="TutorialControls"/>, optionally <c>id:item</c>) may be used now.</summary>
    bool IsAllowed(string control);

    /// <summary>Whether a request may be submitted now: the step's own order, or something harmless (a read, ACK).</summary>
    bool Allows(CommandRequest request);

    /// <summary>"Follow the guide — step N of 23": the tooltip on a control greyed out by the rails.</summary>
    string BlockedTooltip { get; }

    /// <summary>"Let's stick to the checklist — next is: …": the crew's answer to an off-script order.</summary>
    string RedirectLine { get; }
}

public enum TutorialRailsPhase
{
    /// <summary>The step waits for the player's action.</summary>
    Do,

    /// <summary>The step is done; the game is working on it until the next step shows.</summary>
    Wait,

    /// <summary>The crew task the step started failed or was stopped: the same order again.</summary>
    Retry,
}

/// <summary>A numbered railed step and where it stands.</summary>
/// <param name="Order">The order the step's send button submits (the variant's own when it has one); null for a control step.</param>
public sealed record TutorialRailsStep(
    int Number,
    int Total,
    string BeatId,
    string Anchor,
    string? Title,
    TutorialRailData Rail,
    TutorialOrder? Order,
    TutorialRailsPhase Phase)
{
    /// <summary>"Step 4 of 23".</summary>
    public string Header => $"Step {Number} of {Total}";

    /// <summary>What the player does (or waits for) now.</summary>
    public string Instruction => Phase switch
    {
        TutorialRailsPhase.Wait => Rail.Wait ?? "Wait — the next step is coming up.",
        TutorialRailsPhase.Retry => Order is null ? "Try again: click Propose, then Execute, then Confirm." : "Try again: click Send.",
        _ => Rail.Do,
    };
}
