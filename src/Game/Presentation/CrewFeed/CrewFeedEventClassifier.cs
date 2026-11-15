using Usa.Simulation.Commands;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Presentation.CrewFeed;

/// <summary>
/// Picks the simulation events the crew visibly react to: a warning or master alarm going off or escalating
/// (alarmed), the last such alarm clearing or splashdown (relief), an order to them being accepted (nod).
/// First contact comes from comms, not the simulation, so the host notifies it directly.
/// </summary>
public sealed class CrewFeedEventClassifier
{
    private readonly Dictionary<string, AlarmLevel> _loudAlarms = new(StringComparer.Ordinal);

    public bool TryClassify(ISimEvent simEvent, out CrewFeedNotice notice)
    {
        ArgumentNullException.ThrowIfNull(simEvent);
        notice = default;
        switch (simEvent)
        {
            case AlarmRaised { Level: >= AlarmLevel.Warning } raised:
                if (_loudAlarms.TryGetValue(raised.AlarmId, out var level) && level >= raised.Level)
                {
                    _loudAlarms[raised.AlarmId] = raised.Level;
                    return false;
                }

                _loudAlarms[raised.AlarmId] = raised.Level;
                notice = new CrewFeedNotice(null, CrewFeedEvent.Alarm);
                return true;
            case AlarmRaised quiet:
                return QuietDown(quiet.AlarmId, out notice);
            case AlarmCleared cleared:
                return QuietDown(cleared.AlarmId, out notice);
            case Splashdown:
                _loudAlarms.Clear();
                notice = new CrewFeedNotice(null, CrewFeedEvent.Relief);
                return true;
            case CommandAccepted { Assignee: { } assignee }:
                notice = new CrewFeedNotice(assignee, CrewFeedEvent.Acknowledge);
                return true;
            default:
                return false;
        }
    }

    private bool QuietDown(string alarmId, out CrewFeedNotice notice)
    {
        notice = default;
        if (!_loudAlarms.Remove(alarmId) || _loudAlarms.Count > 0) return false;
        notice = new CrewFeedNotice(null, CrewFeedEvent.Relief);
        return true;
    }
}
