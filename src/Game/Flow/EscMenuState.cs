namespace Usa.Game.Flow;

/// <summary>The Esc menu during a mission (GDD 3.2, D12): any state other than <see cref="Closed"/> freezes the simulation with <c>FreezeReason.Menu</c>.</summary>
public enum EscMenuState
{
    Closed,
    Open,
    ConfirmRestart,
    ConfirmAbandon,
}
