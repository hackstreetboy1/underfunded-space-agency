using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Header;

/// <summary>Time-compression indicator and the "Advance to next event" button (UI.md 3.1, GDD 3.2).</summary>
/// <param name="RateText"><c>1×</c> … <c>240×</c>.</param>
/// <param name="ReasonText">Small reason label, e.g. <c>critical alarm</c>; null when there is none.</param>
/// <param name="CanAdvance">The Advance button is enabled.</param>
public sealed record CompressionIndicator(string RateText, string? ReasonText, bool IsCompressed, bool CanAdvance, bool IsAdvancing)
{
    public static CompressionIndicator From(CompressionSnapshot compression, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(compression);
        ArgumentNullException.ThrowIfNull(words);
        return new CompressionIndicator(
            ReadoutFormatter.FormatCompressionRate(compression.Rate),
            words.CompressionReason(compression.Reason),
            compression.Rate > 1.5,
            compression.CanAdvanceToNextEvent && !compression.IsAdvancingToEvent,
            compression.IsAdvancingToEvent);
    }
}
