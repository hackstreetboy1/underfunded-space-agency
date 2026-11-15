using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Crew;

/// <summary>The crew cards in display order, one per astronaut, kept by id across snapshots.</summary>
public sealed class CrewPanelViewModel : ViewModelBase
{
    private readonly StatusLanguage _status;
    private readonly ConsoleWords _words;
    private readonly ObservableList<CrewCardViewModel> _cards = new();
    private readonly Signal<CrewId> _talkRequested = new();

    public CrewPanelViewModel(StatusLanguage status, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(words);
        _status = status;
        _words = words;
    }

    public ObservableList<CrewCardViewModel> Cards => _cards;

    public Signal<CrewId> TalkRequested => _talkRequested;

    public CrewCardViewModel? Find(CrewId id)
    {
        foreach (var card in _cards)
        {
            if (card.Id == id) return card;
        }
        return null;
    }

    public void Apply(IReadOnlyList<CrewMemberSnapshot> crew)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        KeyedSync.Sync(
            _cards,
            crew,
            static member => member.Id,
            static card => card.Id,
            member =>
            {
                var card = new CrewCardViewModel(member.Id, _talkRequested, _status, _words);
                card.Apply(member);
                return card;
            },
            static (card, member) =>
            {
                card.Apply(member);
                return card;
            });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            foreach (var card in _cards) card.Dispose();
        }
        base.Dispose(disposing);
    }
}
