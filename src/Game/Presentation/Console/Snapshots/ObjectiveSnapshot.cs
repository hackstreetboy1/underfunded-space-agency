using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <param name="Label">Short label for the header icon, e.g. <c>O2</c> or <c>Dock</c>.</param>
public sealed record ObjectiveSnapshot(ObjectiveId Id, string Label, ObjectiveState State);
