using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>
/// Fixed crew openers. Production serves them from the content library's <c>CrewPhraseBook</c>, so
/// <c>content/crew_phrases.json</c> has one loader and one validator.
/// </summary>
public interface ICrewPhraseSource
{
    /// <summary>The member's openers of one category in authored order; empty when there are none.</summary>
    IReadOnlyList<CrewOpener> Phrases(CrewId crew, CrewPhraseCategory category);
}

/// <param name="Id">Stable phrase id such as <c>cal.ack.01</c>.</param>
public sealed record CrewOpener(string Id, string Text);

public enum CrewPhraseCategory
{
    Acknowledge,
    Standby,
    Stop,
    Refusal,
    Clarification,
}
