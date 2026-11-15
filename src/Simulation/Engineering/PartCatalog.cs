using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <summary>Every spare part type and connector type, sorted by id.</summary>
public sealed class PartCatalog
{
    public const string SchemaTag = "parts/1";
    public const string DefaultPath = "catalog/parts.json";

    private readonly Dictionary<PartId, PartDefinition> _parts;
    private readonly Dictionary<string, ConnectorTypeDefinition> _connectors;

    private PartCatalog(PartDefinition[] parts, ConnectorTypeDefinition[] connectors)
    {
        Parts = parts;
        ConnectorTypes = connectors;
        _parts = parts.ToDictionary(p => p.Id);
        _connectors = connectors.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    /// <summary>Sorted by id.</summary>
    public IReadOnlyList<PartDefinition> Parts { get; }

    public IReadOnlyList<ConnectorTypeDefinition> ConnectorTypes { get; }

    public static PartCatalog Load(DefinitionLoader loader, string relativePath = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return Create(loader.Load<PartsFile>(relativePath, SchemaTag), relativePath);
    }

    /// <exception cref="DefinitionException">Every problem in the file.</exception>
    public static PartCatalog Create(PartsFile file, string fileLabel)
    {
        ArgumentNullException.ThrowIfNull(file);
        var errors = new DefinitionErrors();

        var connectorIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < file.ConnectorTypes.Count; i++)
        {
            var connector = file.ConnectorTypes[i];
            var path = $"$.connectorTypes[{i}]";
            if (!IsSnakeCase(connector.Id)) errors.Add(fileLabel, path + ".id", $"'{connector.Id}' is not a snake_case id.");
            else if (!connectorIds.Add(connector.Id)) errors.Add(fileLabel, path + ".id", $"Duplicate connector type '{connector.Id}'.");
            if (string.IsNullOrWhiteSpace(connector.Name)) errors.Add(fileLabel, path + ".name", "Name is required.");
        }

        var partIds = new HashSet<PartId>();
        for (var i = 0; i < file.Parts.Count; i++)
        {
            var part = file.Parts[i];
            var path = $"$.parts[{i}]";
            if (!partIds.Add(part.Id)) errors.Add(fileLabel, path + ".id", $"Duplicate part '{part.Id}'.");
            if (string.IsNullOrWhiteSpace(part.Name)) errors.Add(fileLabel, path + ".name", "Name is required.");
            if (part.Connectors.Count is not (0 or 2))
                errors.Add(fileLabel, path + ".connectors", "A part has two connectors (an assembly part) or none.");
            for (var c = 0; c < part.Connectors.Count; c++)
            {
                if (!connectorIds.Contains(part.Connectors[c]))
                    errors.Add(fileLabel, $"{path}.connectors[{c}]", $"Unknown connector type '{part.Connectors[c]}'.");
            }
            if (!double.IsFinite(part.LengthM) || part.LengthM < 0) errors.Add(fileLabel, path + ".lengthM", "Must be >= 0.");
            if (!double.IsFinite(part.CurrentRatingA) || part.CurrentRatingA < 0)
                errors.Add(fileLabel, path + ".currentRatingA", "Must be >= 0.");
            if (part.IsAssemblyPart && part.CurrentRatingA <= 0)
                errors.Add(fileLabel, path + ".currentRatingA", "Parts with connectors need a current rating.");
            if ((part.IsProtection || part.Kind is PartKind.Harness or PartKind.Adapter) && !part.IsAssemblyPart)
                errors.Add(fileLabel, path + ".connectors", "Harnesses, adapters, fuses and breakers need two connectors.");
            if (part.Kind == PartKind.Harness && part.LengthM <= 0)
                errors.Add(fileLabel, path + ".lengthM", "A harness needs a length.");
            if (part.IsTool && part.IsAssemblyPart)
                errors.Add(fileLabel, path + ".connectors", "A tool is not an assembly part and has no connectors.");
            if (part.IsTool && part.ConsumedOnFailure)
                errors.Add(fileLabel, path + ".consumedOnFailure", "A tool is never consumed.");
        }
        errors.ThrowIfAny();

        return new PartCatalog(
            [.. file.Parts.OrderBy(p => p.Id)],
            [.. file.ConnectorTypes.OrderBy(c => c.Id, StringComparer.Ordinal)]);
    }

    public bool Contains(PartId id) => _parts.ContainsKey(id);

    public bool TryGet(PartId id, out PartDefinition part) => _parts.TryGetValue(id, out part!);

    public PartDefinition Get(PartId id) =>
        _parts.TryGetValue(id, out var part) ? part : throw new KeyNotFoundException($"Unknown part '{id}'.");

    /// <summary>True for a part of kind <see cref="PartKind.Tool"/>; hand it to the crew task binder so tasks never consume tools.</summary>
    public bool IsTool(PartId id) => _parts.TryGetValue(id, out var part) && part.IsTool;

    public bool IsConnectorType(string connectorId) => _connectors.ContainsKey(connectorId);

    /// <summary>Display name of a connector type, falling back to the id.</summary>
    public string ConnectorName(string connectorId) => _connectors.TryGetValue(connectorId, out var c) ? c.Name : connectorId;

    internal static bool IsSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')) return false;
        }
        return true;
    }
}
