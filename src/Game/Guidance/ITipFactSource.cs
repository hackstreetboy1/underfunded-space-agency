using System.Diagnostics.CodeAnalysis;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Guidance;

/// <summary>What the ground knows about a fact a tip references (implemented by the integration step over the knowledge readouts).</summary>
public interface ITipFactSource
{
    /// <returns>False when the fact is not in the catalog; the tip then shows no chip for it and never sends it.</returns>
    bool TryDescribe(FactId fact, MissionTime now, [NotNullWhen(true)] out TipFactInfo? info);
}

/// <param name="Label">Catalog label, e.g. <c>OAMS propellant</c>. Sent to the rephraser.</param>
/// <param name="ChipLabel">Card chip text, which may carry the known reading (<c>OAMS propellant 212 kg · 06:58</c>). Never sent.</param>
/// <param name="Band">Qualitative band the ground already has (<c>falling</c>, <c>LOW</c>), or null. Sent only when the fact is not unknown.</param>
public sealed record TipFactInfo(string Label, string ChipLabel, ReadoutKnowledge Knowledge, string? Band = null);
