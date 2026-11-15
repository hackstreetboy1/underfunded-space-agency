using Usa.Game.Content;
using Usa.Simulation.Commands;

namespace Usa.Game.Flow.Screens;

/// <summary>
/// The caption under the loading screen's Astronomy Picture of the Day: "Astronomy Picture of the Day — date", the
/// picture's title, and the credit ("NASA", or "Image: holder" when the picture names a copyright holder). The credit is
/// always shown. The title is shown only when it passes the AI guardrails' forbidden-name check, because APOD titles may
/// name real programmes the game never names (no guardrails loaded: no title); the picture's explanation is never shown.
/// </summary>
public sealed record ApodCaption(string Heading, string? Title, string Credit)
{
    public const int MaxTitleLength = 120;

    public static ApodCaption Create(string date, string? title, string? copyright, AiGuardrails? guardrails, StringTable strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        var heading = strings.Format("briefing.loadingApodHeading", new Dictionary<string, string>(StringComparer.Ordinal) { ["date"] = date });
        var holder = Flatten(copyright);
        var credit = holder.Length == 0
            ? strings.Get("credits.apodCreditAgency")
            : strings.Format("credits.apodCreditHolder", new Dictionary<string, string>(StringComparer.Ordinal) { ["holder"] = holder });
        return new ApodCaption(heading, ShowableTitle(title, guardrails), credit);
    }

    /// <summary>The title as shown, or null when it is empty, too long or names something the game never names.</summary>
    public static string? ShowableTitle(string? title, AiGuardrails? guardrails)
    {
        if (guardrails is null) return null;
        var flat = Flatten(title);
        if (flat.Length == 0 || flat.Length > MaxTitleLength) return null;
        return guardrails.FindForbiddenWord(flat) is null ? flat : null;
    }

    private static string Flatten(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
