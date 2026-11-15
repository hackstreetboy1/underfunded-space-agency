using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <summary>Mission spares for one difficulty (<c>missions/&lt;id&gt;/inventory.&lt;difficulty&gt;.json</c>).</summary>
public sealed record InventoryDefinition(string Schema, string Mission, string Difficulty, IReadOnlyList<InventoryItem> Items)
    : IVersionedDefinition
{
    public const string SchemaTag = "inventory/1";

    public static string PathFor(string missionId, string difficultyId) => $"missions/{missionId}/inventory.{difficultyId}.json";

    public static InventoryDefinition Load(DefinitionLoader loader, string relativePath, PartCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var definition = loader.Load<InventoryDefinition>(relativePath, SchemaTag);
        definition.Validate(relativePath, catalog);
        return definition;
    }

    /// <exception cref="DefinitionException">Unknown or duplicate parts, negative counts.</exception>
    public void Validate(string file, PartCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var errors = new DefinitionErrors();
        var seen = new HashSet<PartId>();
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (!catalog.Contains(item.Part)) errors.Add(file, $"$.items[{i}].part", $"Unknown part '{item.Part}'.");
            if (!seen.Add(item.Part)) errors.Add(file, $"$.items[{i}].part", $"Part '{item.Part}' is listed twice.");
            if (item.Count < 0) errors.Add(file, $"$.items[{i}].count", "Count must be >= 0.");
        }
        errors.ThrowIfAny();
    }
}

public sealed record InventoryItem(PartId Part, int Count);
