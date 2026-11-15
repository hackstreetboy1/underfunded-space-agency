using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <summary>
/// A serializable snapshot of a workspace board (the <c>install_repair</c> command parameter and save data).
/// Part <c>i</c> of <see cref="Parts"/> is instance <c>Instances[i]</c> in <see cref="Links"/>, or instance
/// <c>i + 1</c> when <see cref="Instances"/> is omitted (the canonical form from <see cref="AssemblyGraph.ToDesign"/>).
/// </summary>
/// <param name="Instances">Live instance ids, kept by <see cref="AssemblyGraph.ToSavedDesign"/> so a resumed board keeps them.</param>
/// <param name="NextInstance">The id the next placed part gets; 0 means "after the highest instance".</param>
public sealed record AssemblyDesign(
    string GoalId,
    ComponentId Sink,
    IReadOnlyList<PartId> Parts,
    IReadOnlyList<AssemblyLink> Links,
    IReadOnlyList<int>? Instances = null,
    int NextInstance = 0);

public readonly record struct AssemblyLink(PortRef A, PortRef B);
