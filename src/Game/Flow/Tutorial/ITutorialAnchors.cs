using Usa.Simulation.Guidance;

namespace Usa.Game.Flow.Tutorial;

/// <summary>Anchor id (<see cref="TutorialAnchorIds"/>) to the anchored control's global rectangle in UI pixels; implemented by the mission console.</summary>
public interface ITutorialAnchors
{
    /// <returns>Null when the anchor is unknown or not on screen (the callout then hides until it is).</returns>
    LayoutRect? Find(string anchorId);

    /// <summary>
    /// Like <see cref="Find"/>, and says whether the rectangle is the anchor itself or a step on the way to it (the tab or
    /// drawer button to open first).
    /// </summary>
    TutorialAnchorHit? Resolve(string anchorId) => Find(anchorId) is { } rect ? new TutorialAnchorHit(rect, true) : null;

    /// <summary>
    /// The global rectangle of the External 3D view while it is the visible main view (the player is watching the pod), else
    /// null: no callout or highlight ring may overlap it.
    /// </summary>
    LayoutRect? TryGetAvoidRect() => null;

    /// <summary>
    /// A callout's send button: submits <paramref name="order"/> through the run's structured command path (the one quick
    /// commands and accepted pre-fills use, never the AI), opening the usual confirmation first when the order needs one.
    /// </summary>
    /// <returns>Whether the order went out or is waiting on its confirmation; false when no run is bound or it was refused.</returns>
    bool SendOrder(TutorialOrder order) => false;

    /// <summary>
    /// A token for what an anchor in <see cref="TutorialAnchorIds.CompletesOnChange"/> has set: a dropdown's selected item (so
    /// picking from its list, not just opening it, completes the beat) or whether a confirmed burn order is held (Confirm by
    /// click or Enter completes it, Cancel does not); null when the anchor has no such state or is not built yet.
    /// </summary>
    string? StateOf(string anchorId) => null;

    /// <summary>
    /// Whether the dropdown at <paramref name="anchorId"/> has <paramref name="item"/> (an engine or recovery zone id) chosen: a
    /// railed dropdown step completes on its choice, even when it was already selected when the step showed.
    /// </summary>
    bool IsChosen(string anchorId, string item) => false;
}

/// <param name="IsTarget">False while the rectangle is the tab or button that reveals the anchor.</param>
public readonly record struct TutorialAnchorHit(LayoutRect Rect, bool IsTarget);
