namespace Usa.Simulation.Engineering;

public enum InventoryResult
{
    Ok,
    UnknownPart,
    InvalidQuantity,
    NotEnoughAvailable,
    NotEnoughReserved,

    /// <summary>Tools are checked by the tasks that need them (<c>inventory.&lt;tool&gt;.count</c>), never reserved or consumed.</summary>
    ToolNotReservable,
}
