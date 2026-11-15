using Usa.Simulation.Data;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>Root of <c>catalog/actions.json</c>.</summary>
public sealed record ActionsFile(string Schema, IReadOnlyList<ActionDefinition> Actions) : IVersionedDefinition
{
    public const string SchemaTag = "actions/1";
    public const string DefaultPath = "catalog/actions.json";
}
