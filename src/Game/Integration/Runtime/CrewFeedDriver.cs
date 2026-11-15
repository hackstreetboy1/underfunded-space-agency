using Usa.Game.Comms;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.CrewFeed;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Attitude;
using Usa.Simulation.Crew;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// One <see cref="CrewFeedStateMapper"/> per crew member, fed from the session: classified simulation events
/// (<see cref="CrewFeedEventClassifier"/>), each member's first transcript line (first contact), and per frame their
/// biomedical telemetry, task or glance read, EVA location, the crewed vehicle's body rate, the g load and the voice loop.
/// </summary>
/// <remarks>
/// The simulation inputs are read from the session's state paths (<c>crew.&lt;id&gt;.*</c>, the crewed vehicle's
/// <c>attitude.*.rate_total_dps</c>, <c>flight.g_load</c>) and only when the frame reader's stamp moved; the voice loop, the
/// glance reads and the feeds' own animation still update every frame. Nothing here allocates once warm.
/// </remarks>
public sealed class CrewFeedDriver
{
    private const string GLoadPath = "flight.g_load";

    private readonly MissionSession _session;
    private readonly SessionFrameReader? _frame;
    private readonly ICrewVoiceActivity? _voices;
    private readonly Func<CrewId, bool>? _readingGauge;
    private readonly CrewFeedEventClassifier _classifier = new();
    private readonly Dictionary<CrewId, CrewFeedStateMapper> _mappers = [];
    private readonly Member[] _members;
    private readonly HashSet<CrewId> _contacted = [];
    private readonly HashSet<string> _suitedLocations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CrewActivity> _taskActivities = new(StringComparer.Ordinal);
    private readonly string _ratePath;
    private SessionFrameStamp _readAt;
    private bool _read;
    private double _rateDps;
    private double _gLoad;

    /// <param name="readingGauge">Whether a crew member is reading a gauge on request (the comms adapter's pending glances).</param>
    /// <param name="frame">The runtime's frame reader; without one the simulation inputs are re-read every frame.</param>
    public CrewFeedDriver(MissionSession session, ExpressionSet expressions, IMonotonicClock clock, ICrewVoiceActivity? voices = null,
        CrewFeedSettings? settings = null, Func<CrewId, bool>? readingGauge = null, SessionFrameReader? frame = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(expressions);
        ArgumentNullException.ThrowIfNull(clock);
        _session = session;
        _frame = frame;
        _voices = voices;
        _readingGauge = readingGauge;
        _ratePath = $"{StatePaths.Attitude}{session.Data.Flight.Vehicles.CrewedId}.{AttitudeFields.RateTotalDps}";
        var crew = session.Data.Scenario.Mission.Crew;
        _members = new Member[crew.Count];
        for (var i = 0; i < crew.Count; i++)
        {
            var mapper = new CrewFeedStateMapper(expressions, clock, session.Seed, crew[i].Value, settings);
            _mappers[crew[i]] = mapper;
            _members[i] = new Member(crew[i], mapper);
        }
        foreach (var location in session.Data.Locations.Locations)
        {
            if (location.IsSuited) _suitedLocations.Add(location.Id);
        }
        Feeds = _mappers;
    }

    /// <summary>Each crew member's feed; read <see cref="CrewFeedStateMapper.Current"/> after <see cref="Update"/>.</summary>
    public IReadOnlyDictionary<CrewId, CrewFeedStateMapper> Feeds { get; }

    public void Observe(ISimEvent simEvent)
    {
        if (!_classifier.TryClassify(simEvent, out var notice)) return;
        for (var i = 0; i < _members.Length; i++)
        {
            if (notice.AppliesTo(_members[i].Id)) _members[i].Mapper.Notify(notice.Event);
        }
    }

    /// <summary>An event from before a resume: keeps the classifier's alarm set current, the crew do not react to it.</summary>
    public void ObserveHistory(ISimEvent simEvent) => _classifier.TryClassify(simEvent, out _);

    /// <summary>A crew member's first line on the loop waves at the ground.</summary>
    public void OnTranscriptChanged(TranscriptChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Kind != TranscriptChangeKind.Added || change.Message.Speaker is not { } crew) return;
        if (change.Message.Kind is TranscriptMessageKind.PlayerText or TranscriptMessageKind.PlayerVoice or TranscriptMessageKind.System
            or TranscriptMessageKind.Pending) return;
        if (_mappers.TryGetValue(crew, out var mapper) && _contacted.Add(crew)) mapper.Notify(CrewFeedEvent.FirstContact);
    }

    /// <summary>Per frame, after the frame's events were observed.</summary>
    public void Update()
    {
        if (_frame is null || !_read || _frame.Stamp != _readAt)
        {
            if (_frame is not null) _readAt = _frame.Stamp;
            _read = true;
            ReadSimulation();
        }

        for (var i = 0; i < _members.Length; i++)
        {
            var member = _members[i];
            if (!member.Present) continue;
            var voice = _voices?.For(member.Id) ?? CrewVoiceState.Silent;
            var activity = member.TaskActivity
                ?? (member.Eva ? CrewActivity.Eva : _readingGauge?.Invoke(member.Id) == true ? CrewActivity.GlanceRead : CrewActivity.None);
            member.Mapper.Update(new CrewFeedInput(
                member.Alive,
                member.Band,
                voice.Speaking,
                voice.LevelDbfs,
                voice.Listening,
                activity,
                _rateDps,
                _gLoad));
        }
    }

    private void ReadSimulation()
    {
        var state = _session.State;
        _rateDps = state.TryGet(_ratePath, out var rate) && rate.Kind == StateValueKind.Number ? rate.AsNumber() : 0;
        _gLoad = state.TryGet(GLoadPath, out var g) && g.Kind == StateValueKind.Number ? g.AsNumber() : 0;
        for (var i = 0; i < _members.Length; i++)
        {
            var member = _members[i];
            member.Present = state.TryGet(member.AlivePath, out var alive) && alive.Kind == StateValueKind.Bool;
            if (!member.Present) continue;
            member.Alive = alive.AsBool();
            member.Band = state.TryGet(member.BandPath, out var band) && band.Kind == StateValueKind.Text ? Band(band.AsText()) : HealthBand.Stable;
            member.Eva = state.TryGet(member.LocationPath, out var location) && location.Kind == StateValueKind.Text &&
                _suitedLocations.Contains(location.AsText());
            member.TaskActivity = state.TryGet(member.ActivityPath, out var task) && task.Kind == StateValueKind.Text ? TaskActivity(task.AsText()) : null;
        }
    }

    /// <summary>The task a member is performing (their <c>activity</c> path holds its id) → the feed's activity; cached per task id.</summary>
    private CrewActivity? TaskActivity(string taskId)
    {
        if (taskId.Length == 0) return null;
        if (_taskActivities.TryGetValue(taskId, out var known)) return known;
        if (!_session.Data.Tasks.TryGet(new TaskId(taskId), out var definition)) return null;
        var activity = CrewActivities.From(definition.Kind);
        _taskActivities[taskId] = activity;
        return activity;
    }

    private static HealthBand Band(string word) => word switch
    {
        "stressed" => HealthBand.Stressed,
        "impaired" => HealthBand.Impaired,
        "critical" => HealthBand.Critical,
        "deceased" => HealthBand.Deceased,
        _ => HealthBand.Stable,
    };

    private sealed class Member(CrewId id, CrewFeedStateMapper mapper)
    {
        public CrewId Id { get; } = id;

        public CrewFeedStateMapper Mapper { get; } = mapper;

        public string AlivePath { get; } = $"{StatePaths.Crew}{id.Value}.alive";

        public string BandPath { get; } = $"{StatePaths.Crew}{id.Value}.band";

        public string LocationPath { get; } = $"{StatePaths.Crew}{id.Value}.location";

        public string ActivityPath { get; } = $"{StatePaths.Crew}{id.Value}.activity";

        /// <summary>The roster serves this member (the snapshot path skipped members it did not list).</summary>
        public bool Present { get; set; }

        public bool Alive { get; set; }

        public HealthBand Band { get; set; }

        public bool Eva { get; set; }

        public CrewActivity? TaskActivity { get; set; }
    }
}
