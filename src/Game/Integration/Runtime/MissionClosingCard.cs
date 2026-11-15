using Usa.Game.Content;
using Usa.Game.Presentation.EntryHud;
using Usa.Simulation.Kernel;
using Usa.Simulation.Scenario;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// The console's closing beat once the mission has ended (GDD D10, UI 4.3): the player stays on the console until they press
/// "Proceed to debrief" (<see cref="MissionRuntimeContext.ProceedToDebrief"/>). After a recovery the recovery card
/// (<see cref="SplashdownCardModel"/>) is the closing card and this record only says the run is over; when every crew member
/// was lost it is a card of its own: brief, quiet and respectful (no red, no alarm), naming the lost crew. Its words come from
/// <c>content/strings.json</c> (<c>console.closing*</c>); the constants are the same copy, used when a key is missing.
/// </summary>
/// <param name="Lost">Full names of the crew lost on this flight, in mission crew order.</param>
public sealed record MissionClosingCard(
    MissionOutcomeKind Outcome,
    MissionTime EndedAt,
    string Title,
    string Body,
    string Note,
    IReadOnlyList<string> Lost)
{
    public const string ProceedLabel = SplashdownCardModel.ProceedLabel;
    public const string RecoveredTitle = "RECOVERED";
    public const string CrewLostTitle = "MISSION OVER";
    public const string RecoveredBody = "The crew is aboard the recovery ship.";
    public const string DebriefNote = "The debrief shows what you knew at each decision.";
    public const string CrewLostBodyTemplate = "Contact with {names} has ended. The crew was lost by MET {met}.";
    public const string TheCrew = "the crew";
    public const string NamesAndTemplate = "{names} and {last}";

    /// <summary>The recovery card carries this outcome; the console needs no extra card.</summary>
    public bool UsesRecoveryCard => Outcome == MissionOutcomeKind.Recovered;

    /// <param name="strings">The player-facing words; null (or a missing key) uses the constants.</param>
    public static MissionClosingCard Build(MissionOutcomeKind outcome, MissionTime endedAt, IReadOnlyList<string> lost, ITextSource? strings = null)
    {
        ArgumentNullException.ThrowIfNull(lost);
        var note = Word(strings, "console.closingDebriefNote", DebriefNote);
        return outcome == MissionOutcomeKind.Recovered
            ? new MissionClosingCard(outcome, endedAt, Word(strings, "console.closingRecoveredTitle", RecoveredTitle),
                Word(strings, "console.closingRecoveredBody", RecoveredBody), note, lost)
            : new MissionClosingCard(outcome, endedAt, Word(strings, "console.closingCrewLostTitle", CrewLostTitle),
                CrewLostBody(lost, endedAt, strings), note, lost);
    }

    /// <summary><c>Contact with Cal Brandt and June Okafor has ended. The crew was lost by MET 07:41:12.</c></summary>
    public static string CrewLostBody(IReadOnlyList<string> lost, MissionTime endedAt, ITextSource? strings = null)
    {
        ArgumentNullException.ThrowIfNull(lost);
        var names = lost.Count switch
        {
            0 => Word(strings, "console.closingTheCrew", TheCrew),
            1 => lost[0],
            _ => TextTemplate.Fill(Word(strings, "console.closingNamesAnd", NamesAndTemplate),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["names"] = string.Join(", ", lost.Take(lost.Count - 1)),
                    ["last"] = lost[^1],
                }),
        };
        return TextTemplate.Fill(Word(strings, "console.closingCrewLostBody", CrewLostBodyTemplate),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["names"] = names, ["met"] = endedAt.ToString() });
    }

    private static string Word(ITextSource? strings, string key, string fallback)
    {
        if (strings is null) return fallback;
        try
        {
            var text = strings.Get(key);
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (KeyNotFoundException)
        {
            return fallback;
        }
    }
}
