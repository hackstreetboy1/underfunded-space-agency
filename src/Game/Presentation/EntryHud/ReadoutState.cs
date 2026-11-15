namespace Usa.Game.Presentation.EntryHud;

/// <summary>Knowledge state of a HUD value (GDD 4.1, UI 2.3). The default is <see cref="Unknown"/>.</summary>
public enum ReadoutState
{
    Unknown,
    Known,
    Stale,
    Estimated,
}
