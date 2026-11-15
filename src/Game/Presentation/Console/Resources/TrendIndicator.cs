using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>Trend arrow and signed rate, e.g. <c>▼ −4.2 kg/h</c>.</summary>
public sealed record TrendIndicator(TrendDirection Direction, string Text)
{
    public static TrendIndicator None { get; } = new(TrendDirection.None, string.Empty);

    /// <summary>From a sanitized readout: unknown values and ground bands never show a trend.</summary>
    public static TrendIndicator From(ReadoutSnapshot sanitized)
    {
        ArgumentNullException.ThrowIfNull(sanitized);
        if (sanitized.Knowledge is ReadoutKnowledge.Unknown or ReadoutKnowledge.GroundBand) return None;
        if (sanitized.TrendPerHour is not { } perHour || !double.IsFinite(perHour)) return None;

        var text = ReadoutFormatter.FormatTrend(perHour, sanitized.Unit);
        var direction = text.StartsWith('▲') ? TrendDirection.Rising
            : text.StartsWith('▼') ? TrendDirection.Falling
            : TrendDirection.Steady;
        return new TrendIndicator(direction, text);
    }
}
