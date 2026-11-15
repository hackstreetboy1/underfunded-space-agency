namespace Usa.Game.Presentation.Console.MainViews;

/// <summary>The centre view tabs in display order (UI.md 3.6); keys <c>1</c>–<c>4</c> follow this order.</summary>
public enum MainViewKind
{
    Orbit,
    External,
    Diagram,

    /// <summary>Only while a repair workspace is open.</summary>
    Workspace,
}
