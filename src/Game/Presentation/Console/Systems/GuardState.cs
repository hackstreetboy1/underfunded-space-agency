namespace Usa.Game.Presentation.Console.Systems;

/// <summary>The hinged cover of a guarded switch (UI.md 3.9).</summary>
public enum GuardState
{
    NotGuarded,
    Closed,
    Open,

    /// <summary>The change was requested; the confirmation dialog is up.</summary>
    AwaitingConfirmation,
}
