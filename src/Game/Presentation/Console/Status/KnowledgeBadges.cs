using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Status;

/// <summary>Knowledge-state icons and colours (UI.md 2.3).</summary>
public static class KnowledgeBadges
{
    public static readonly KnowledgeVisual Known = new(ReadoutKnowledge.Known, StatusShape.None, string.Empty, null, ColourTokens.TextHi, false);
    public static readonly KnowledgeVisual Stale = new(ReadoutKnowledge.Stale, StatusShape.Clock, "◷", ColourTokens.TextMid, ColourTokens.TextMid, false);
    public static readonly KnowledgeVisual Unknown = new(ReadoutKnowledge.Unknown, StatusShape.DashedCircle, "◌", ColourTokens.Unknown, ColourTokens.Unknown, true);
    public static readonly KnowledgeVisual Estimated = new(ReadoutKnowledge.Estimated, StatusShape.Approximate, "≈", ColourTokens.Info, ColourTokens.TextHi, false);
    public static readonly KnowledgeVisual Conflict = new(ReadoutKnowledge.Conflict, StatusShape.Exchange, "⇄", ColourTokens.Conflict, ColourTokens.TextHi, false);
    public static readonly KnowledgeVisual GroundBand = new(ReadoutKnowledge.GroundBand, StatusShape.GroundBand, "▤", ColourTokens.TextMid, ColourTokens.TextHi, false);

    public static KnowledgeVisual For(ReadoutKnowledge knowledge) => knowledge switch
    {
        ReadoutKnowledge.Known => Known,
        ReadoutKnowledge.Stale => Stale,
        ReadoutKnowledge.Estimated => Estimated,
        ReadoutKnowledge.Conflict => Conflict,
        ReadoutKnowledge.GroundBand => GroundBand,
        _ => Unknown,
    };
}
