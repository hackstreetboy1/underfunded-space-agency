using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>Each crew member's own known-fact view. Implemented over the Knowledge area.</summary>
public interface IKnownFacts
{
    /// <summary>True when <paramref name="crew"/> observed or was told <paramref name="fact"/>.</summary>
    bool Knows(CrewId crew, FactId fact);
}
