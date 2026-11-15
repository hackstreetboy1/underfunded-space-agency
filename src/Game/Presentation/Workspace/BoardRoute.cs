namespace Usa.Game.Presentation.Workspace;

/// <summary>The wired route from the source as it stands (board legend, Figma G5-01a <c>Route 3.0 m</c>).</summary>
/// <param name="LengthM">Summed length of the parts wired in series from the source.</param>
/// <param name="Parts">Parts on that route.</param>
/// <param name="ReachesSink">The route ends at the sink.</param>
/// <param name="Text">e.g. <c>Route 3.0 m</c>, <c>Route 2.0 m so far</c>; empty when no goal is open.</param>
public sealed record BoardRoute(double LengthM, int Parts, bool ReachesSink, string Text)
{
    public static BoardRoute None { get; } = new(0, 0, false, "");
}
