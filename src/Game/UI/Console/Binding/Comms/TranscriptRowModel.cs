using Usa.Game.Comms;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.UI.Console.Binding.Comms;

/// <summary>How a transcript message is laid out (UI.md 3.5).</summary>
public enum TranscriptRowLayout
{
    /// <summary>Right-aligned on <c>bg.3</c>.</summary>
    Player,

    /// <summary>Left-aligned on <c>bg.2</c>, with the speaker's name.</summary>
    Crew,

    /// <summary>Centred meta text.</summary>
    System,

    /// <summary>"Okafor is responding…" with the pulse and cancel.</summary>
    Pending,
}

/// <summary>Display strings for one transcript message.</summary>
/// <param name="Meta">Player: <c>You → Okafor · voice · 06:57:48</c>; crew and system: the MET.</param>
/// <param name="Badge"><c>REPORT</c> or <c>REFUSED</c>; null otherwise.</param>
/// <param name="FactChips">One per resolved fact, <c>212 kg · 06:58</c>.</param>
/// <param name="Options">Quick replies of a clarification.</param>
/// <param name="ReadOfferLabel">The crew's offer to read a fact, e.g. <c>Read OAMS propellant</c>.</param>
public sealed record TranscriptRowModel(
    string Id,
    TranscriptRowLayout Layout,
    string Text,
    string? Speaker,
    string Meta,
    string? Badge,
    string? Reason,
    IReadOnlyList<string> FactChips,
    IReadOnlyList<string> Options,
    string? ReadOfferLabel,
    string? RequestId)
{
    /// <summary>Who a player message went to (Gate 2 decision 4); null for the whole crew and for other messages.</summary>
    public string? Addressee { get; init; }

    /// <param name="addresseeName">Who the player spoke to (Gate 2 decision 4); null for the whole crew.</param>
    public static TranscriptRowModel From(TranscriptMessage message, string? addresseeName, CommsWords words)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(words);
        var met = ReadoutFormatter.FormatMet(message.Met);
        var layout = message.Kind switch
        {
            TranscriptMessageKind.PlayerText or TranscriptMessageKind.PlayerVoice => TranscriptRowLayout.Player,
            TranscriptMessageKind.System => TranscriptRowLayout.System,
            TranscriptMessageKind.Pending => TranscriptRowLayout.Pending,
            _ => TranscriptRowLayout.Crew,
        };
        var meta = layout == TranscriptRowLayout.Player ? PlayerMeta(message, addresseeName, words, met) : met;
        var badge = message.Kind switch
        {
            TranscriptMessageKind.ObservationReport => words.Report,
            TranscriptMessageKind.Refusal => words.Refused,
            _ => null,
        };
        var chips = new string[message.Facts.Count];
        for (var i = 0; i < chips.Length; i++)
        {
            var fact = message.Facts[i];
            chips[i] = fact.Observation is { } observation
                ? fact.Text + ConsoleText.Separator + ReadoutFormatter.FormatMetShort(observation.Time)
                : fact.Text;
        }
        return new TranscriptRowModel(
            message.Id, layout, message.Text, message.SpeakerName, meta, badge,
            message.Kind == TranscriptMessageKind.Refusal ? message.ReasonText : null,
            chips, message.Options, message.Offer?.Label, message.RequestId)
        {
            Addressee = layout == TranscriptRowLayout.Player && !string.IsNullOrWhiteSpace(addresseeName) ? addresseeName : null,
        };
    }

    private static string PlayerMeta(TranscriptMessage message, string? addresseeName, CommsWords words, string met)
    {
        var who = string.IsNullOrWhiteSpace(addresseeName) ? words.You : words.You + " → " + addresseeName;
        return message.Kind == TranscriptMessageKind.PlayerVoice
            ? who + ConsoleText.Separator + words.Voice + ConsoleText.Separator + met
            : who + ConsoleText.Separator + met;
    }
}
