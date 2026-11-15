using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Workspace;

/// <summary>The ghost of a part being dragged, snapped to the grid, with why it can't be dropped there.</summary>
/// <param name="Card">The card being moved; null when dragging a new part from the tray.</param>
public sealed record DragPreview(
    PartId Part,
    CardId? Card,
    GridPoint Cell,
    int XPx,
    int YPx,
    int WidthPx,
    int HeightPx,
    EditProblem Problem,
    AssemblyFailure Failure,
    string Message)
{
    public bool IsValid => Problem == EditProblem.None;
}

/// <summary>A wire being drawn from <see cref="From"/>; <see cref="Hover"/> is the port under the pointer, if any.</summary>
/// <param name="HoverFailure">Why the wire can't end at <see cref="Hover"/> (UI: red ✕); None when it can.</param>
/// <param name="HoverMessage">Plain-language reason shown on hover; empty when the wire can end there.</param>
public sealed record WireGesture(BoardPort From, BoardPort? Hover, AssemblyFailure HoverFailure, string HoverMessage)
{
    public bool HoverValid => Hover is not null && HoverFailure == AssemblyFailure.None;
}

public enum ContextActionKind
{
    RemovePart,
    Disconnect,
}

/// <summary>A right-click action on a card or fixed node.</summary>
public sealed record ContextAction(ContextActionKind Kind, CardId Card, BoardPort Port, string Label);
