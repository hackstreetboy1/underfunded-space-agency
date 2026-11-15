using Usa.Game.Presentation.Console.Status;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Header;

/// <summary>One small objective icon in the header summary.</summary>
public sealed record ObjectiveChip(ObjectiveId Id, string Label, StatusVisual Visual);
