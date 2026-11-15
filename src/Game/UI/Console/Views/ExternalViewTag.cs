namespace Usa.Game.UI.Console.Views;

/// <summary>A small tag drawn over a part of the 3D view (UI.md 4.4), e.g. <c>STALE 22m</c> on an older observation.</summary>
/// <param name="ComponentId">Vehicle-data component id, <c>tandem/oams/thruster_08</c>.</param>
public sealed record ExternalViewTag(string ComponentId, string Text);
