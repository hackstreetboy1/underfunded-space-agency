using Usa.Game.Presentation.Console.Status;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>The forecast block at the right end of the resource strip (Figma G1-03: three lines and an EST badge).</summary>
/// <param name="Text">The whole forecast on one line, <c>Safe autonomy ≈ 7.5–9 h · Limit: re-entry battery · Confidence: medium</c>
/// (tooltips, narration, compact layouts).</param>
/// <param name="Coverage">Whether the range covers the planned return.</param>
/// <param name="Available">A forecast exists (the breakdown can open).</param>
public sealed record ForecastPill(string Text, StatusVisual Coverage, bool Available)
{
    /// <summary>First line, <c>Safe autonomy</c>.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Second line, the range: <c>≈ 7.5–9 h</c>; <c>—</c> while pending.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Third line, <c>Limit: re-entry battery · Confidence: medium</c>; <c>Forecast pending</c> before the first one.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>The estimate badge (<c>EST</c>); null while pending, since nothing is estimated yet.</summary>
    public string? Badge { get; init; }
}
