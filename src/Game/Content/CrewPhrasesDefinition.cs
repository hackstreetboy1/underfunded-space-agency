using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Game.Content;

/// <summary>Root of <c>content/crew_phrases.json</c> (schema <c>crew_phrases/1</c>): fixed openers per crew member.</summary>
/// <param name="Categories">Description per phrase kind (snake_case <see cref="CrewPhraseKind"/>), for writers.</param>
/// <param name="Notes">For writers, never shown.</param>
public sealed record CrewPhrasesDefinition(
    string Schema,
    IReadOnlyDictionary<string, string> Categories,
    IReadOnlyDictionary<CrewId, IReadOnlyList<CrewPhraseEntry>> Members,
    string? Notes = null) : IVersionedDefinition
{
    public const string SchemaTag = "crew_phrases/1";
    public const string RelativePath = "content/crew_phrases.json";
}

/// <param name="Id">Stable id, e.g. <c>cal.ack.01</c>; it names the cached TTS clip.</param>
public sealed record CrewPhraseEntry(string Id, CrewPhraseKind Category, string Text);

public enum CrewPhraseKind
{
    /// <summary>A clear, possible, low-impact order was accepted.</summary>
    Acknowledge,

    /// <summary>A glance read or check is starting.</summary>
    Standby,

    /// <summary>The player said stop; the current task is being stopped.</summary>
    Stop,

    /// <summary>Opener before the validator's reason.</summary>
    Refusal,

    /// <summary>Opener before the one clarifying question.</summary>
    Clarification,
}

/// <param name="CacheKey">
/// <see cref="Id"/> plus a hash of <see cref="Text"/>: stable across runs and machines, and new when the
/// writer changes the line, so a stale TTS clip is never played.
/// </param>
public sealed record CrewPhrase(string Id, CrewId Crew, CrewPhraseKind Kind, string Text, string CacheKey);
