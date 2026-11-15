using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <summary>Root of <c>catalog/parts.json</c>.</summary>
public sealed record PartsFile(string Schema, IReadOnlyList<ConnectorTypeDefinition> ConnectorTypes, IReadOnlyList<PartDefinition> Parts)
    : IVersionedDefinition;

/// <param name="Id">snake_case connector id used by parts and repair ports, e.g. <c>d_type</c>.</param>
public sealed record ConnectorTypeDefinition(string Id, string Name);

/// <summary>A spare part type.</summary>
/// <param name="Connectors">Connector type at each end. Assembly parts have exactly two (series elements); others none.</param>
/// <param name="LengthM">Length the part adds to a route.</param>
/// <param name="CurrentRatingA">Continuous rating for conductors; trip rating for fuses and breakers.</param>
/// <param name="ConsumedOnFailure">Lost when the installation attempt fails (e.g. a blown fuse).</param>
public sealed record PartDefinition(
    PartId Id,
    string Name,
    PartKind Kind,
    IReadOnlyList<string> Connectors,
    double LengthM = 0,
    double CurrentRatingA = 0,
    bool ConsumedOnFailure = false,
    string Description = "")
{
    public bool IsAssemblyPart => Connectors.Count == 2;

    public bool IsProtection => Kind is PartKind.Fuse or PartKind.Breaker;

    /// <summary>Checked for availability but never consumed (see <see cref="Inventory"/>).</summary>
    public bool IsTool => Kind == PartKind.Tool;

    /// <summary>
    /// <see cref="Name"/> without a trailing bracket that only restates the part's length or rating
    /// (<c>inline fuse (10 A)</c> → <c>inline fuse</c>), for text that shows the figure next to the name.
    /// </summary>
    public string ShortName
    {
        get
        {
            if (!Name.EndsWith(')')) return Name;
            var open = Name.LastIndexOf(" (", StringComparison.Ordinal);
            if (open <= 0) return Name;
            var spec = Name[(open + 2)..^1];
            var restated = (CurrentRatingA > 0 && spec == EngineeringFormat.Amps(CurrentRatingA))
                           || (LengthM > 0 && spec == EngineeringFormat.Metres(LengthM));
            return restated ? Name[..open] : Name;
        }
    }
}
