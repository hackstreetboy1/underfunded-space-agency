namespace Usa.Game.Presentation.ViewModels;

/// <summary>Display strings for one readout, ready for labels (UI.md 2.3).</summary>
/// <param name="Value">Main value with unit, e.g. <c>212 kg</c>, <c>—</c>, <c>≈7.5–9 h</c>, <c>LOW</c>.</param>
/// <param name="SecondaryValue">The second, stacked value of a conflict; otherwise null.</param>
/// <param name="Meta">Meta line, e.g. <c>Okafor · MET 06:58</c>; null when there is nothing to attribute.</param>
/// <param name="Badge">Knowledge badge, e.g. <c>STALE 12m</c>, <c>UNKNOWN</c>, <c>EST</c>; null when known and fresh.</param>
/// <param name="Trend">Trend arrow and rate, e.g. <c>▼ −4.2 kg/h</c>; null without a trend.</param>
/// <param name="Dimmed">Render the value in <c>text.mid</c> (stale).</param>
/// <param name="CanAsk">Offer the "Ask" affordance (unknown).</param>
public sealed record ReadoutText(string Value, string? SecondaryValue, string? Meta, string? Badge, string? Trend, bool Dimmed, bool CanAsk)
{
    /// <summary>Meta line of <see cref="SecondaryValue"/>, e.g. <c>Brandt · MET 07:02</c>; null when unattributed.</summary>
    public string? SecondaryMeta { get; init; }
}
