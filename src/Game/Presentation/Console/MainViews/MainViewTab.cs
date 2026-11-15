namespace Usa.Game.Presentation.Console.MainViews;

/// <summary>One centre view tab as shown: <c>Orbit</c> with its key hint <c>1</c>.</summary>
/// <param name="Enabled">False greys the tab out (Workspace while no repair is open).</param>
public sealed record MainViewTab(MainViewKind View, string Label, string KeyHint, bool Enabled);
