using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>A glance read: fixed <paramref name="Facts"/>, or the fact named by the <paramref name="FactParam"/> argument.</summary>
/// <param name="DelayS">Time until the reading is reported.</param>
public sealed record GlanceDefinition(double DelayS, IReadOnlyList<FactId>? Facts = null, string? FactParam = null);
