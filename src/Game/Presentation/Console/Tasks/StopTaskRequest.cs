using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Tasks;

/// <summary>The player pressed Stop on the task slot.</summary>
/// <param name="NeedsConfirmation">The task is unsafe to interrupt: show the confirmation dialog first.</param>
public sealed record StopTaskRequest(TaskId TaskId, bool NeedsConfirmation);
