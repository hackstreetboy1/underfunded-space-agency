using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Workspace;

/// <summary>One inventory tray card.</summary>
/// <param name="IconKey">Workspace part icon (UI 8.1): harness, extension, adapter, fuse, breaker, tape, lioh_canister, generic_spare.</param>
/// <param name="Name">Short name, capitalised (<c>Inline fuse</c>); length and rating are in <paramref name="LengthText"/> and <paramref name="RatingText"/>.</param>
/// <param name="Quantity">How many more can be placed: spares on board and not reserved, minus those already on the board. Never negative.</param>
/// <param name="OnBoard">Cards of this part on the board.</param>
/// <param name="Shortfall">Cards on the board beyond the available spares (stock taken by another task after placing); the inventory check explains it.</param>
/// <param name="LengthText">e.g. <c>2 m</c>; null for parts without length.</param>
/// <param name="RatingText">e.g. <c>15 A</c>; null for parts without a current rating.</param>
/// <param name="CarriesCurrent">False for tape, canisters and other parts without connectors; dropping them on the board is refused.</param>
public sealed record PartCard(
    PartId Part,
    string IconKey,
    string Name,
    PartKind Kind,
    int Quantity,
    int OnBoard,
    int Shortfall,
    string? LengthText,
    string? RatingText,
    IReadOnlyList<ConnectorChip> Connectors,
    bool CarriesCurrent)
{
    public bool CanPlace => CarriesCurrent && Quantity > 0;
}

/// <param name="ShapeKey">Connector shape icon key; the connector type id.</param>
public sealed record ConnectorChip(string Id, string Name, string ShapeKey);
