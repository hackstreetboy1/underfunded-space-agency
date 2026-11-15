using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;

namespace Usa.Game.Presentation.Console.Header;

/// <param name="Word">Chip text, e.g. <c>DOCKED OPS</c>.</param>
public sealed record PhaseChip(MissionPhase Phase, string Word, string ColourToken)
{
    public static PhaseChip For(MissionPhase phase, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var colour = phase switch
        {
            MissionPhase.Emergency => ColourTokens.Critical,
            MissionPhase.ReturnPrep or MissionPhase.Entry => ColourTokens.Caution,
            MissionPhase.Recovery or MissionPhase.Ended => ColourTokens.Info,
            _ => ColourTokens.TextHi,
        };
        return new PhaseChip(phase, words.PhaseWord(phase), colour);
    }
}
