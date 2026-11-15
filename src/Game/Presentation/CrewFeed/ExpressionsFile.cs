using Usa.Simulation.Data;

namespace Usa.Game.Presentation.CrewFeed;

/// <summary>Root of <c>data/crew/expressions.json</c> (schema <c>expressions/1</c>).</summary>
/// <param name="Presets">Preset name to shape-key weights; keys left out are 0.</param>
public sealed record ExpressionsFile(
    string Schema,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Presets,
    string? Notes = null) : IVersionedDefinition
{
    public const string SchemaTag = "expressions/1";
    public const string RelativePath = "crew/expressions.json";
}
