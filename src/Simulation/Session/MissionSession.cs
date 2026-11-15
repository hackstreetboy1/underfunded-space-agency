using Usa.Simulation.Attitude;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew;
using Usa.Simulation.Data;
using Usa.Simulation.Engineering;
using Usa.Simulation.Expressions;
using Usa.Simulation.Flight;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Ports;
using Usa.Simulation.Scoring;
using Usa.Simulation.Time;

namespace Usa.Simulation.Session;

/// <summary>
/// The facade the host talks to (SCRIPTING 11.9). Every state change goes through <see cref="Submit"/> (commands) or
/// <see cref="Advance"/> (time); everything else reads. Built by <see cref="Load"/>; composition and tick order are documented on
/// <see cref="SessionComposer"/>.
/// </summary>
public sealed class MissionSession
{
    private readonly SessionParts _parts;
    private readonly TutorialCommandHandler _tutorialCommands;
    private long _nextCommand;

    private MissionSession(SessionParts parts, IDataSource source, string sessionId, string? runTag)
    {
        _parts = parts;
        _tutorialCommands = new TutorialCommandHandler(parts.Tutorial, parts.Log, parts.Executed);
        Source = source;
        SessionId = sessionId;
        CommandIdPrefix = runTag is null ? sessionId : $"{sessionId}-{runTag}";
    }

    /// <summary>"&lt;mission&gt;-&lt;seed&gt;".</summary>
    public string SessionId { get; }

    /// <summary>
    /// The prefix of <see cref="NextCommandId"/>: <see cref="SessionId"/>, plus "-&lt;run tag&gt;" when the host gave one. The
    /// game tags every launch so ids from a restart or a resume never repeat an earlier run's (the AI proxy caches request
    /// ids for 10 minutes); replays and scenario runs pass none and stay reproducible.
    /// </summary>
    public string CommandIdPrefix { get; }

    public string MissionId => _parts.Data.MissionId;

    /// <summary>
    /// This mission's view of the data (<see cref="MissionDataSource"/>): read mission-scoped files such as
    /// <c>catalog/actions.json</c> or <c>crew/crew.json</c> through it to get the shared entries plus the mission's own.
    /// </summary>
    public IDataSource Source { get; }

    public string DifficultyId => _parts.Data.Difficulty.Id;

    public ulong Seed => _parts.Seed;

    public MissionTime Now => _parts.Clock.Now;

    public bool IsFrozen => _parts.Clock.IsFrozen;

    public FreezeReason FreezeReasons => _parts.Clock.FreezeReasons;

    /// <summary>Read access to every state path (STATE_PATHS.md).</summary>
    public IStateQuery State => _parts.State;

    public CommandLog CommandLog => _parts.Log;

    public ActionCatalog Actions => _parts.Data.Actions;

    public MissionData Data => _parts.Data;

    public GuidanceDirector Guidance => _parts.Guidance;

    public TutorialDirector Tutorial => _parts.Tutorial;

    /// <summary>Host-side rate control; feed it <see cref="CompressionInputs"/> each frame.</summary>
    public TimeCompressionDirector TimeCompression => _parts.TimeCompression;

    public KnowledgeBase Knowledge => _parts.Knowledge;

    public FactTemplateResolver FactTemplates => new(_parts.Data.Facts, _parts.Knowledge);

    public RepairWorkspace Workspace => _parts.Repairs;

    public AlarmMonitor Alarms => _parts.Alarms;

    /// <summary>Hashes recorded every 60 s MET.</summary>
    public IReadOnlyList<StateHashSample> HashSamples => _parts.Hashes.Samples;

    /// <summary>Set while the player reads a report (guidance holds the stuck timer). Not reproduced by replays.</summary>
    public bool ReadingReference
    {
        get => _parts.GuidanceBridge.ReadingReference;
        set => _parts.GuidanceBridge.ReadingReference = value;
    }

    /// <summary>Loads every data file and composes the session at the mission's start MET.</summary>
    /// <exception cref="DefinitionException">Every data problem found.</exception>
    /// <param name="runTag">Lower-case letters and digits that make this run's command ids unique (see <see cref="CommandIdPrefix"/>).</param>
    public static MissionSession Load(IDataSource source, string missionId, string difficultyId, ulong seed, string? runTag = null)
    {
        if (runTag is not null && !IsRunTag(runTag))
            throw new ArgumentException("A run tag is one or more lower-case ASCII letters and digits.", nameof(runTag));
        var compiler = new ExpressionCompiler();
        var view = MissionDataSource.For(source, missionId);
        var data = MissionData.Load(view, missionId, difficultyId, compiler);
        return new MissionSession(SessionComposer.Compose(data, seed, compiler), view, $"{missionId}-{seed}", runTag);
    }

    /// <summary>
    /// Rebuilds a run from its seed and command log (resume and replay): each entry is submitted at its MET (workspace designs
    /// are <c>register_repair_design</c> entries), and the session is left at <paramref name="until"/> or at the last entry.
    /// </summary>
    /// <param name="chunkTicks">Ticks per clock call while catching up; the result never depends on it.</param>
    /// <param name="runTag">Tags the ids of commands issued after the replay (resume); logged entries keep their own ids.</param>
    public static MissionSession Replay(
        IDataSource source,
        string missionId,
        string difficultyId,
        ulong seed,
        CommandLog log,
        MissionTime? until = null,
        int chunkTicks = int.MaxValue,
        string? runTag = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        var session = Load(source, missionId, difficultyId, seed, runTag);
        foreach (var entry in log.Entries)
        {
            session.RunTo(new MissionTime(entry.MetMs), chunkTicks);
            session.Submit(entry.Request);
        }
        if (until is { } end) session.RunTo(end, chunkTicks);
        return session;
    }

    private static bool IsRunTag(string tag)
    {
        if (tag.Length == 0) return false;
        foreach (var c in tag)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c)) return false;
        }
        return true;
    }

    public void Freeze(FreezeReason reason) => _parts.Clock.Freeze(reason);

    public void Unfreeze(FreezeReason reason) => _parts.Clock.Unfreeze(reason);

    /// <summary>Runs whole 50 ms ticks up to <paramref name="target"/>, at most <paramref name="maxTicks"/>.</summary>
    /// <returns>Ticks run; 0 while frozen.</returns>
    public int Advance(MissionTime target, int maxTicks) => _parts.Clock.AdvanceTo(target, maxTicks);

    /// <summary>A fresh deterministic command id for a new request.</summary>
    /// <remarks>The one command-id source of a run: console, workspace, scenario scripts and comms all draw from it.</remarks>
    public CommandId NextCommandId() => CommandId.Create(CommandIdPrefix, ++_nextCommand);

    /// <summary>
    /// The single input path (SCRIPTING 11.3), at the current MET. Crew tasks are checked against their own arguments,
    /// preconditions and parts during validation, so they are refused before confirmation (review X9). Guided-first-run commands
    /// (<see cref="TutorialCommands"/>) are logged and replayed the same way but run by the session itself, outside the catalog.
    /// </summary>
    public ValidationResult Submit(CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TutorialCommands.IsTutorialCommand(request.Action.Value)
            ? _tutorialCommands.Submit(request, Now)
            : _parts.Processor.Submit(request, Now);
    }

    /// <summary>The meta action that carries a workspace design through the command log.</summary>
    public const string RegisterDesignAction = "register_repair_design";

    /// <summary>
    /// Submits a workspace design as <see cref="RegisterDesignAction"/> (workspace source), so the command log replays it; a design
    /// that does not parse or fails the digital test is refused (<see cref="RepairWorkspace.Check"/>).
    /// </summary>
    public ValidationResult RegisterDesign(string assemblyId, AssemblyDesign design)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyId);
        ArgumentNullException.ThrowIfNull(design);
        var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal)
        {
            [RepairDesignArgs.Assembly] = CommandArg.Of(assemblyId),
            [RepairDesignArgs.Design] = CommandArg.Of(RepairDesignCodec.Serialize(design)),
        };
        return Submit(new CommandRequest(NextCommandId(), CommandSource.Workspace, new ActionId(RegisterDesignAction), null, args, Confirmed: true));
    }

    /// <summary>
    /// Takes the events a <see cref="Replay"/> (resume) produced while it rebuilt the run, in publish order, and clears the buffer.
    /// Call it once right after <see cref="Replay"/> and route the result to history consumers only (e.g. the debrief recorder):
    /// they happened before the resume and must not reach the live presentation. Afterwards <see cref="DrainEvents"/> yields only
    /// events of ticks run after the resume.
    /// </summary>
    public IReadOnlyList<ISimEvent> DrainReplayHistory()
    {
        var history = new List<ISimEvent>();
        _parts.Events.Drain(history.Add);
        return history;
    }

    /// <summary>Hands every buffered event to <paramref name="sink"/> in publish order and clears the buffer.</summary>
    public void DrainEvents(Action<ISimEvent> sink) => _parts.Events.Drain(sink);

    /// <summary>SHA-256 over every area's state in a fixed order.</summary>
    public string StateHash() => StateHasher.Compute(_parts.HashOrder);

    /// <summary>The scoring input for the run so far (call when the mission ends, or any time for a preview).</summary>
    public MissionRecord BuildRecord() =>
        MissionRecordBuilder.Build(_parts.Data.SessionConfig.Record, _parts.Director, _parts.Roster, _parts.Health, _parts.Flight, _parts.Tracker, _parts.AttitudeBridge.ForcedPropellantKg);

    /// <summary>Grades the run as it stands.</summary>
    public ScoreBreakdown Evaluate() => _parts.Scoring.Evaluate(BuildRecord());

    /// <summary>Ends the run: stops the crew slot and grades it.</summary>
    public ScoreBreakdown Finish()
    {
        _parts.Runner.Stop(Now, Crew.Tasks.TaskStopReason.Aborted);
        return Evaluate();
    }

    public CompressionInputs CompressionInputs() => SnapshotBuilder.Compression(_parts);

    public SessionSnapshot Snapshot() => SnapshotBuilder.Build(_parts);

    /// <summary>
    /// Read-only orbits for the map and relative views (see <see cref="FlightTrackView"/>), with the ground's estimates. Built
    /// once per change: repeated calls return the same instance until MET or the flight state moves on.
    /// </summary>
    public FlightTrackView FlightTrack()
    {
        var flight = _parts.Flight;
        var key = (Now, flight.Docked, flight.Crewed.Time, flight.Crewed.State, flight.Crewed.Phase, flight.Target?.Time, flight.Target?.State);
        if (_flightTrack is not null && _flightTrackKey == key) return _flightTrack;
        _flightTrackKey = key;
        return _flightTrack = FlightTrackView.Build(flight, Now, _parts.Tracking);
    }

    /// <summary>One vehicle's attitude without allocating (the game reads it every frame).</summary>
    /// <returns>False for a vehicle attitude does not fly.</returns>
    public bool TryGetAttitude(string vehicleId, out AttitudeReading reading)
    {
        if (!_parts.Attitude.TryGetVehicle(vehicleId, out var vehicle))
        {
            reading = default;
            return false;
        }
        reading = new AttitudeReading(
            vehicle.Orientation, vehicle.RateRollDegPerSec, vehicle.RatePitchDegPerSec, vehicle.RateYawDegPerSec, vehicle.RateTotalDegPerSec,
            vehicle.DockedWithId);
        return true;
    }

    /// <summary>The ground's picture of a vehicle's orbit (X8); null when it is not tracked (a docked target).</summary>
    public NavEstimate? NavEstimate(string vehicleId) => _parts.Tracking.Estimate(vehicleId, Now);

    /// <summary>The ground mass ledger's estimate of one vehicle now (X8); null for an unknown vehicle. Allocates.</summary>
    public Knowledge.Ledger.GroundMassEstimate? GroundMass(string vehicleId) => _parts.MassLedgers.Estimate(vehicleId);

    /// <summary>
    /// What the ground knows changed <paramref name="vehicleId"/>'s mass (X8 ledger input; build the
    /// <see cref="Knowledge.Ledger.GroundMassLedgerSpec"/> itself from <see cref="Data"/>'s vehicles): its jettisoned sections and
    /// fired retro rockets (their propellant is a spent dry-mass id), and every commanded burn's engine-spec draw from its tanks.
    /// Allocates; call on demand.
    /// </summary>
    public Knowledge.Ledger.KnownMassEvents KnownMassEvents(string vehicleId) => _parts.MassLedgers.Events(vehicleId);

    internal SessionParts Parts => _parts;

    private FlightTrackView? _flightTrack;
    private (MissionTime, bool, MissionTime, StateVector, FlightPhase, MissionTime?, StateVector?) _flightTrackKey;

    private void RunTo(MissionTime target, int chunkTicks)
    {
        while (Now.Milliseconds + SimulationClock.BaseTickMs <= target.Milliseconds)
        {
            if (_parts.Clock.AdvanceTo(target, Math.Max(1, chunkTicks)) == 0) break;
        }
    }
}
