using System.Globalization;
using Godot;
using Usa.Game.Presentation.CrewFeed;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Crew;

namespace Usa.Game.Presentation.Views.Preview;

/// <summary>
/// Driver of <c>res://scenes/crew/CrewFeedPreview.tscn</c>: both crew feeds side by side, each driven by its own
/// <see cref="CrewFeedStateMapper"/> through a scripted cycle of situations (4 s each), to check the clips, blends,
/// gestures, faces and lighting. User argument after <c>--</c>: <c>--feed-scenario=&lt;name&gt;</c> holds one situation
/// (names as in the overlay; <c>lost</c> is only reachable this way, since it is final).
/// </summary>
public sealed partial class CrewFeedPreview : Control
{
    private const double ScenarioSec = 4.0;
    private const string ExpressionsPath = "res://data/crew/expressions.json";

    private static readonly CrewFeedInput Calm = CrewFeedInput.Calm;

    private static readonly (string Name, CrewFeedInput Input)[] Scenarios =
    [
        ("idle", Calm),
        ("wave", Calm with { RecentEvent = CrewFeedEvent.FirstContact }),
        ("talk", Calm with { Speaking = true, SpeechLevelDbfs = -16 }),
        ("listen", Calm with { Listening = true }),
        ("nod", Calm with { Listening = true, RecentEvent = CrewFeedEvent.Acknowledge }),
        ("work", Calm with { Activity = CrewActivity.Repair }),
        ("read_gauge", Calm with { Activity = CrewActivity.GlanceRead }),
        ("alarm", Calm with { Health = HealthBand.Stressed, RecentEvent = CrewFeedEvent.Alarm }),
        ("stressed", Calm with { Health = HealthBand.Stressed }),
        ("spin_strain", Calm with { AngularRateDegPerSec = 150 }),
        ("g_load", Calm with { GLoad = 6 }),
        ("impaired", Calm with { Health = HealthBand.Impaired }),
        ("relief", Calm with { RecentEvent = CrewFeedEvent.Relief }),
    ];

    private static readonly CrewFeedInput Lost = Calm with { Alive = false, Health = HealthBand.Deceased };

    private readonly StopwatchClock _clock = new();
    private readonly List<CrewFeedStateMapper> _feeds = [];
    private Label _info = null!;
    private int _scenario;
    private double _scenarioSec;
    private bool _eventSent;
    private bool _hold;
    private bool _lost;
    private double _timeSec;

    public override void _Ready()
    {
        _info = GetNode<Label>("%Info");
        var expressions = ExpressionSet.Parse(Godot.FileAccess.GetFileAsString(ExpressionsPath));
        foreach (var name in new[] { "Cal", "June" })
        {
            var view = GetNode<CrewFeedView>($"%{name}");
            var feed = new CrewFeedStateMapper(expressions, _clock, 20261115, view.CrewId);
            view.Follow(feed);
            _feeds.Add(feed);
        }
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            const string prefix = "--feed-scenario=";
            if (!arg.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var wanted = arg[prefix.Length..];
            _lost = wanted == "lost";
            var index = Array.FindIndex(Scenarios, s => s.Name == wanted);
            _hold = _lost || index >= 0;
            _scenario = Math.Max(index, 0);
        }
    }

    public override void _Process(double delta)
    {
        _timeSec += delta;
        _scenarioSec += delta;
        if (!_hold && _scenarioSec >= ScenarioSec)
        {
            _scenarioSec = 0;
            _eventSent = false;
            _scenario = (_scenario + 1) % Scenarios.Length;
        }

        var (name, input) = _lost ? ("lost", Lost) : Scenarios[_scenario];
        if (_eventSent) input = input with { RecentEvent = CrewFeedEvent.None };
        _eventSent = true;
        if (input.Speaking) input = input with { SpeechLevelDbfs = -18 + 8 * Math.Sin(_timeSec * 9.0) };
        foreach (var feed in _feeds) feed.Update(input);

        var frame = _feeds[0].Current;
        _info.Text = string.Create(CultureInfo.InvariantCulture,
            $"situation {name}   state {frame.State}   face {frame.Expression}   spin {frame.SpinIntensity:0.00}   g {frame.GIntensity:0.00}{(frame.Lost ? "   LOST" : string.Empty)}");
    }
}
