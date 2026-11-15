namespace Usa.Game.UI.Console.Binding.Comms;

/// <summary>
/// Fixed words of the comms panel that no view model carries yet (Figma G2-08); the scene supplies them so they stay with
/// the design text until they move to <c>content/strings.json</c>.
/// </summary>
/// <param name="You">The player in a bubble's meta line.</param>
/// <param name="Voice">Marks a spoken message.</param>
/// <param name="Crew">The addressee selector's "everyone" entry.</param>
/// <param name="Report">Badge of an automatic observation report.</param>
/// <param name="Refused">Badge of a refusal.</param>
public sealed record CommsWords(string You, string Voice, string Crew, string Report, string Refused);
