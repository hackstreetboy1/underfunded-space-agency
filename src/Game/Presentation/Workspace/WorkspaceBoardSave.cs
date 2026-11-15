using Usa.Simulation.Engineering;

namespace Usa.Game.Presentation.Workspace;

/// <summary>The board being edited, for a save or a retry: the design with live instance ids, and where each card sits.</summary>
/// <param name="CardCells">Cell of the card for <c>Design.Parts[i]</c>, index for index.</param>
public sealed record WorkspaceBoardSave(AssemblyDesign Design, IReadOnlyList<GridPoint> CardCells);
