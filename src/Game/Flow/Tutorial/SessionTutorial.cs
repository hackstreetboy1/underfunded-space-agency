using Usa.Simulation.Commands;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Flow.Tutorial;

/// <summary><see cref="ITutorialSession"/> over a <see cref="MissionSession"/>: commands carry the session's own command ids.</summary>
public sealed class SessionTutorial(MissionSession session) : ITutorialSession
{
    private readonly MissionSession _session = session ?? throw new ArgumentNullException(nameof(session));

    public bool IsEnabled => _session.Tutorial.IsEnabled;

    public TutorialCallout? Active
    {
        get
        {
            if (_session.Tutorial.ActiveBeatId is not { } id) return null;
            foreach (var beat in _session.Data.Tutorial.Beats)
            {
                if (beat.Id == id) return new TutorialCallout(beat.Id, beat.Anchor, beat.ResolveText(_session.State), beat.Title, beat.ResolveOrder(_session.State));
            }
            return null;
        }
    }

    public bool Dismiss(string beatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beatId);
        return Submit(TutorialCommands.DismissBeat, TutorialCommands.BeatArg, CommandArg.Of(beatId));
    }

    public bool SetEnabled(bool enabled) => Submit(TutorialCommands.SetEnabled, TutorialCommands.EnabledArg, CommandArg.Of(enabled));

    private bool Submit(string action, string argName, CommandArg value)
    {
        var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { [argName] = value };
        var request = new CommandRequest(_session.NextCommandId(), CommandSource.QuickCommand, new ActionId(action), null, args, Confirmed: true);
        return _session.Submit(request).IsValid;
    }
}
