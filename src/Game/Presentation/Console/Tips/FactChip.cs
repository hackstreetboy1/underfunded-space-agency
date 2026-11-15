using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Tips;

/// <summary>An inline fact chip in a tip; clicking it highlights the source panel.</summary>
/// <param name="Badge"><c>STALE</c>, <c>⇄ CONFLICT</c>… when the fact is not known and fresh; otherwise null.</param>
public sealed record FactChip(string Label, string? Badge, KnowledgeVisual Knowledge, SystemsTab? Tab, string? FactKey)
{
    public static FactChip From(TipFactSnapshot fact, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(words);
        return new FactChip(fact.Label, words.FactBadge(fact.Knowledge), KnowledgeBadges.For(fact.Knowledge), fact.Tab, fact.FactKey);
    }
}
