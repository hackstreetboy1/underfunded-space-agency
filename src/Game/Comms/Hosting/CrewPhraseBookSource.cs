using Usa.Game.Content;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms.Hosting;

/// <summary>Serves comms openers from the content library's phrase book (the only loader of <c>crew_phrases.json</c>).</summary>
public sealed class CrewPhraseBookSource : ICrewPhraseSource
{
    private readonly Dictionary<(CrewId, CrewPhraseCategory), IReadOnlyList<CrewOpener>> _openers = [];

    public CrewPhraseBookSource(CrewPhraseBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        foreach (var crew in book.CrewIds)
        {
            foreach (var category in Enum.GetValues<CrewPhraseCategory>())
            {
                var phrases = book.For(crew, KindOf(category));
                if (phrases.Count > 0) _openers[(crew, category)] = phrases.Select(p => new CrewOpener(p.Id, p.Text)).ToArray();
            }
        }
    }

    public IReadOnlyList<CrewOpener> Phrases(CrewId crew, CrewPhraseCategory category) =>
        _openers.TryGetValue((crew, category), out var openers) ? openers : [];

    private static CrewPhraseKind KindOf(CrewPhraseCategory category) => category switch
    {
        CrewPhraseCategory.Acknowledge => CrewPhraseKind.Acknowledge,
        CrewPhraseCategory.Standby => CrewPhraseKind.Standby,
        CrewPhraseCategory.Stop => CrewPhraseKind.Stop,
        CrewPhraseCategory.Refusal => CrewPhraseKind.Refusal,
        CrewPhraseCategory.Clarification => CrewPhraseKind.Clarification,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };
}
