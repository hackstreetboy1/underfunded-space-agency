using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew;

/// <summary>Root of <c>crew/crew.json</c> (schema <c>crew/1</c>).</summary>
public sealed record CrewFileDefinition : IVersionedDefinition
{
    public required string Schema { get; init; }

    public required IReadOnlyList<CrewMemberDefinition> Members { get; init; }

    public required CrewPerformanceDefinition Performance { get; init; }
}

/// <summary>One astronaut. Every <see cref="SkillDomain"/> is rated 1-5; experience is 0-1.</summary>
public sealed record CrewMemberDefinition
{
    public required CrewId Id { get; init; }

    public required string Name { get; init; }

    public required string Role { get; init; }

    public required int Age { get; init; }

    public required string Background { get; init; }

    public required IReadOnlyDictionary<SkillDomain, int> Skills { get; init; }

    public required double Experience { get; init; }

    public required CrewPersona Persona { get; init; }

    /// <summary>Voice id for speech synthesis; empty until voices are chosen.</summary>
    public string VoiceId { get; init; } = "";

    public int Skill(SkillDomain domain) =>
        Skills.TryGetValue(domain, out var level) ? level : throw new KeyNotFoundException($"Crew member '{Id}' has no level for skill '{domain}'.");
}

/// <summary>Dialogue persona handed to the language model; never affects simulation results.</summary>
public sealed record CrewPersona
{
    public required string Personality { get; init; }

    public required string Cadence { get; init; }

    public required string StressExpression { get; init; }

    public required string Humor { get; init; }

    public IReadOnlyList<string> SampleLines { get; init; } = [];
}
