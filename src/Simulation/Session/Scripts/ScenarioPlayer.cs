using System.Globalization;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew;
using Usa.Simulation.Data;
using Usa.Simulation.Engineering;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;
using Usa.Simulation.Vehicle;

namespace Usa.Simulation.Session.Scripts;

/// <summary>
/// Plays a <see cref="ScenarioScript"/> headless through <see cref="MissionSession"/>: one MET second at a time, due steps are
/// submitted at the start of the second (script order), the session advances one second in chunks of <c>chunkTicks</c>, events
/// are drained into event keys and the timeline, and the crewed vehicle (plus the mission's <c>session.json</c> report samples) is sampled. Ends when the mission ends or at the script's
/// <see cref="ScenarioScript.MaxMetS"/>; then expectations are checked and the run is graded.
/// </summary>
public sealed class ScenarioPlayer(IDataSource source)
{
    private const double DefaultRunS = 8 * 3600;
    private const long SecondMs = 1000;

    private readonly IDataSource _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <param name="chunkTicks">Ticks per <see cref="MissionSession.Advance"/> call inside each second (results must not depend on it).</param>
    public ScenarioResult Play(ScenarioScript script, int chunkTicks = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(script);
        var session = MissionSession.Load(_source, script.Mission, script.Difficulty, script.Seed);
        return new Run(session, script, chunkTicks).Execute();
    }

    private sealed class StepState(ScenarioStep step, int index)
    {
        public ScenarioStep Step { get; } = step;
        public int Index { get; } = index;
        public long? DueMs { get; set; }
        public bool Done { get; set; }
        public bool Aimed { get; set; }
        public long FirstAttemptMs { get; set; } = -1;
        public IExpression? When { get; set; }
        public string Name => Step.Label ?? $"#{Index}";
    }

    private sealed class Run
    {
        private readonly MissionSession _session;
        private readonly SessionParts _parts;
        private readonly ScenarioScript _script;
        private readonly int _chunkTicks;
        private readonly List<StepState> _steps = [];
        private readonly Dictionary<string, long> _keys = new(StringComparer.Ordinal);
        private readonly Dictionary<CommandId, StepState> _byCommand = [];
        private readonly List<TimelineEntry> _timeline = [];
        private readonly List<ScenarioSample> _samples = [];
        private string? _estimateZone;
        private readonly List<string> _failures = [];
        private readonly CrewId[] _crew;
        private readonly RealTimeEstimator _realTime;
        private readonly HashSet<string> _activeAlarms = new(StringComparer.Ordinal);
        private readonly string[] _samplePaths;
        private readonly IReadOnlyList<ScenarioSampleDefinition> _extraSamples;
        private readonly string? _incidentKey;

        public Run(MissionSession session, ScenarioScript script, int chunkTicks)
        {
            _session = session;
            _parts = session.Parts;
            _script = script;
            _chunkTicks = Math.Max(1, chunkTicks);
            _crew = _parts.Roster.Members.Select(m => m.Id).ToArray();
            _realTime = new RealTimeEstimator(_parts.Data.TimeCompression);
            var crewed = _parts.Data.CrewedVehicleId;
            _samplePaths =
            [
                $"attitude.{crewed}.rate_roll_dps", $"attitude.{crewed}.rate_total_dps", $"cabin.{crewed}.temp_c", $"cabin.{crewed}.ppco2_mmhg",
                "flight.altitude_km", "flight.periapsis_km",
            ];
            var report = _parts.Data.SessionConfig.Report;
            _extraSamples = report?.Samples ?? [];
            _incidentKey = report?.IncidentEvent is { } incident ? $"event:{incident}" : null;
            for (var i = 0; i < script.Commands.Count; i++)
            {
                var state = new StepState(script.Commands[i], i);
                if (state.Step.AtMetS is { } at) state.DueMs = CeilSecond((long)Math.Round(at * 1000));
                if (state.Step.When is { } when) state.When = Compile(when, $"step {state.Name} when");
                _steps.Add(state);
            }
        }

        public ScenarioResult Execute()
        {
            var endMs = (long)Math.Round((_script.MaxMetS ?? _session.Now.Seconds + DefaultRunS) * 1000);
            long? splashdownRealS = null;
            long? entryRealS = null;
            Sample();
            while (true)
            {
                var nowMs = _session.Now.Milliseconds;
                ResolveTriggers(nowMs);
                var acted = false;
                foreach (var step in _steps)
                {
                    if (!step.Done && step.DueMs is { } due && due <= nowMs) acted |= Execute(step, nowMs);
                }
                if (_parts.Director.IsEnded || nowMs >= endMs) break;

                AdvanceOneSecond(nowMs + SecondMs);
                Drain();
                Sample();
                _realTime.Step(_session.CompressionInputs(), acted);
                if (splashdownRealS is null && _keys.ContainsKey("event:splashdown")) splashdownRealS = (long)_realTime.AutoRealS;
                if (entryRealS is null && _keys.ContainsKey("event:entry_interface")) entryRealS = (long)_realTime.AutoRealS;
            }

            var finalHash = _session.StateHash();
            var ended = _parts.Director.IsEnded ? _session.State.GetText("mission.outcome") : "running";
            foreach (var step in _steps)
            {
                if (!step.Done && !step.Step.Optional) _failures.Add($"step {step.Name} never ran");
            }

            var score = _session.Finish();
            Drain();
            var survivors = (int)_session.State.GetNumber("crew.alive_count");
            var metrics = Metrics();
            var realTime = new RealTimeEstimate(_realTime.AutoRealS, _realTime.WithAdvanceRealS, splashdownRealS ?? _realTime.AutoRealS, entryRealS ?? _realTime.AutoRealS, _realTime.AutoRealSByReason);
            var result = new ScenarioResult(
                _script, ended, _session.Now.Milliseconds, score, survivors, metrics, realTime, _timeline, _samples, _extraSamples,
                new Dictionary<string, long>(_keys, StringComparer.Ordinal), _failures, _session.CommandLog,
                [.. _session.HashSamples], finalHash, Variants());
            ScenarioExpectations.Check(result, _session.State, _parts.Compiler, _failures);
            return result;
        }

        private void AdvanceOneSecond(long targetMs)
        {
            var target = new MissionTime(targetMs);
            while (_session.Now < target)
            {
                if (_session.Advance(target, _chunkTicks) == 0) break;
            }
        }

        private void ResolveTriggers(long nowMs)
        {
            foreach (var step in _steps)
            {
                if (step.Done || step.DueMs is not null) continue;
                long? armedMs = 0;
                if (step.Step.AtEvent is { } key) armedMs = _keys.TryGetValue(key, out var at) ? at : null;
                if (armedMs is not { } armed) continue;
                if (step.When is { } when)
                {
                    if (armed > nowMs || !Holds(when, $"step {step.Name} when")) continue;
                    armed = nowMs;
                }
                step.DueMs = CeilSecond(armed + (long)Math.Round(step.Step.DelayS * 1000));
            }
        }

        /// <returns>True when a command or design was submitted.</returns>
        private bool Execute(StepState step, long nowMs)
        {
            if (step.FirstAttemptMs < 0) step.FirstAttemptMs = nowMs;
            if (step.Step.Design is { } design)
            {
                RegisterDesign(step, design, nowMs);
                return true;
            }
            if (step.Step.Zone is { } zone && !step.Aimed)
            {
                AimRetrofire(step, zone, nowMs);
                return false;
            }

            var action = new ActionId(step.Step.Action!);
            var args = step.Step.Args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
            CrewId? assignee = step.Step.Assignee is { } crew ? new CrewId(crew) : null;
            var request = new CommandRequest(_session.NextCommandId(), CommandSource.QuickCommand, action, assignee, args, step.Step.Confirmed);
            var result = _session.Submit(request);

            switch (result.Status)
            {
                case ValidationStatus.Valid when step.Step.ExpectRefused is not null:
                    Fail(step, nowMs, $"expected refusal '{step.Step.ExpectRefused}' but it was accepted");
                    break;
                case ValidationStatus.Valid:
                    step.Done = true;
                    _byCommand[request.Id] = step;
                    Log(nowMs, "cmd", $"{step.Name}: {Describe(request)}");
                    var kind = _parts.Data.Actions.TryGet(action, out var catalog) ? catalog.Kind : ActionKind.Meta;
                    if (kind != ActionKind.CrewTask) Key("done", step.Name, nowMs);
                    break;
                case ValidationStatus.CrewBusy when step.Step.ExpectRefused is null &&
                                                    nowMs - step.FirstAttemptMs < (long)(step.Step.RetryBusyS * 1000):
                    step.DueMs = nowMs + SecondMs;
                    break;
                case ValidationStatus.Refused or ValidationStatus.CrewBusy when step.Step.ExpectRefused is { } expected &&
                                                                               (expected == "*" || expected == result.ReasonCode):
                    step.Done = true;
                    Key("refused", step.Name, nowMs);
                    Log(nowMs, "refused", $"{step.Name}: {Describe(request)} -> {result.ReasonCode} (expected)");
                    break;
                default:
                    Fail(step, nowMs, $"{Describe(request)} -> {result.Status} {result.ReasonCode} {result.Detail}".TrimEnd());
                    break;
            }
            return true;
        }

        private void Fail(StepState step, long nowMs, string message)
        {
            step.Done = true;
            Key("refused", step.Name, nowMs);
            Log(nowMs, "error", $"{step.Name}: {message}");
            _failures.Add($"{Met(nowMs)} step {step.Name}: {message}");
        }

        private void RegisterDesign(StepState step, ScenarioDesign design, long nowMs)
        {
            step.Done = true;
            var board = _session.Workspace.NewBoard(design.Goal, _session.State);
            var detail = "no board";
            if (board is not null && Chain(board, design.Chain))
            {
                var test = _session.Workspace.Test(board);
                detail = string.Join(", ", test.Failures.Select(c => $"{c.Check}:{c.Reason}"));
                var result = _session.RegisterDesign(design.Assembly, board.ToDesign());
                if (!result.IsValid) detail = $"{result.ReasonCode} {detail}".TrimEnd();
            }
            var ready = _session.Workspace.IsReady(design.Assembly);
            Log(nowMs, "design", $"{step.Name}: {design.Assembly} [{string.Join(" + ", design.Chain)}] -> {(ready ? "ready" : detail)}");
            Key(ready ? "done" : "refused", step.Name, nowMs);
            if (ready != design.ExpectReady)
                _failures.Add($"{Met(nowMs)} step {step.Name}: design {(ready ? "accepted" : "rejected")}, expected {(design.ExpectReady ? "ready" : "rejected")}");
        }

        /// <summary>Places the parts in series from the source, entering each by the end matching the previous connector.</summary>
        private bool Chain(AssemblyGraph board, IReadOnlyList<string> parts)
        {
            var previous = PortRef.Source;
            foreach (var partId in parts)
            {
                var part = new PartId(partId);
                if (!_session.Workspace.Parts.TryGet(part, out var definition)) return false;
                var placed = board.Place(part);
                if (!placed.Succeeded) return false;
                var instance = placed.Instance;
                if (definition.Connectors.Count < 2) continue;
                var connector = board.ConnectorAt(previous);
                var entry = definition.Connectors[1] == connector && definition.Connectors[0] != connector ? 1 : 0;
                if (!board.Connect(previous, new PortRef(instance, entry)).Succeeded) return false;
                previous = new PortRef(instance, 1 - entry);
            }
            return board.Connect(previous, PortRef.Sink).Succeeded;
        }

        private void AimRetrofire(StepState step, string zoneId, long nowMs)
        {
            var leadS = step.Step.LeadS ?? LeadFor(step.Step.Action!);
            var flight = _parts.Flight;
            var proposer = new BurnProposer(flight.Planner);
            var engine = new RetroSequenceEngine(flight.Mission.RetroSequence);
            var earliest = new MissionTime(nowMs + (long)Math.Ceiling(leadS * 1000) + SecondMs);
            var zones = zoneId == "next" ? flight.Mission.Zones.Zones.Select(z => z.Id).ToArray() : [zoneId];
            BurnProposal? best = null;
            foreach (var zone in zones)
            {
                var proposal = proposer.Propose(flight.CreatePreviewState(), new TargetZone(zone), engine, earliest);
                if (proposal.Plan is { } plan && (best?.Plan is not { } current || plan.StartTime < current.StartTime)) best = proposal;
            }
            if (best?.Plan is not { } chosen)
            {
                Fail(step, nowMs, $"no retrofire solution for zone '{zoneId}'");
                return;
            }
            step.Aimed = true;
            step.DueMs = Math.Max(nowMs, (chosen.StartTime.Milliseconds - (long)Math.Round(leadS * 1000)) / SecondMs * SecondMs);
            var zoneName = best.Preview?.LandingZone?.ZoneId ?? "?";
            Log(nowMs, "plan", $"{step.Name}: retro start {Met(chosen.StartTime.Milliseconds)} for {zoneName}, order at {Met(step.DueMs.Value)}");

            // X8: the same plan flown on the ground's estimate (what the game shows the player) must predict the same zone.
            if (_parts.Tracking.PreviewState(flight.CrewedId) is { } estimate)
            {
                var now = new MissionTime(nowMs);
                var preview = flight.Planner.Preview(estimate, chosen, _parts.Tracking.Uncertainty(flight.CrewedId, now), flight.Drogue, flight.Main);
                _estimateZone = preview.LandingZone?.ZoneId ?? "-";
                Log(nowMs, "plan", $"{step.Name}: on the ground estimate the retrofire lands in {_estimateZone}");
            }
        }

        private double LeadFor(string action) =>
            _parts.Data.Actions.TryGet(new ActionId(action), out var catalog) && catalog.Definition.TaskId is { } task &&
            _parts.Data.Tasks.TryGet(task, out var definition)
                ? definition.BaseDurationS
                : 0;

        private void Drain() => _session.DrainEvents(OnEvent);

        private void OnEvent(ISimEvent simEvent)
        {
            var ms = simEvent.Time.Milliseconds;
            switch (simEvent)
            {
                case IncidentTriggered e:
                    Key("incident", e.IncidentId.Value, ms);
                    Log(ms, "incident", e.IncidentId.Value);
                    break;
                case MissionEventRaised e:
                    Key("event", e.EventId, ms);
                    Log(ms, "event", e.EventId);
                    break;
                case PhaseChanged e:
                    Key("phase", e.To, ms);
                    Log(ms, "phase", $"{e.From} -> {e.To}");
                    break;
                case ObjectiveCompleted e:
                    Key("objective", e.ObjectiveId.Value, ms);
                    Log(ms, "objective", $"{e.ObjectiveId} completed");
                    break;
                case ObjectiveFeasibilityChanged e:
                    Log(ms, "objective", $"{e.ObjectiveId} {e.From} -> {e.To} ({e.Cause?.ToString() ?? "-"}, {e.Reason})");
                    break;
                case MissionRuleActivated e:
                    Log(ms, "rule", $"{e.RuleId} active ({e.Cause})");
                    break;
                case CrewTaskStarted e:
                    Log(ms, "task", $"{e.Task} started by {e.Assignee}, {e.DurationS:0} s");
                    break;
                case CrewTaskCompleted e:
                    Key("task_done", e.Task.Value, ms);
                    if (_byCommand.TryGetValue(e.Origin, out var done)) Key("done", done.Name, ms);
                    Log(ms, "task", $"{e.Task} done");
                    break;
                case CrewTaskFailed e:
                    Key("task_failed", e.Task.Value, ms);
                    if (_byCommand.TryGetValue(e.Origin, out var failed)) Key("failed", failed.Name, ms);
                    Log(ms, "task", $"{e.Task} FAILED (p={e.ErrorProbability:0.00})");
                    break;
                case CrewTaskStopped e:
                    if (_byCommand.TryGetValue(e.Origin, out var stopped)) Key("stopped", stopped.Name, ms);
                    Log(ms, "task", $"{e.Task} stopped ({e.Reason}, {e.Progress:P0})");
                    break;
                case ObservationRecorded e:
                    Key("observation", e.Observation.Fact.Value, ms);
                    break;
                case AlarmRaised e:
                    Key("alarm", e.AlarmId, ms);
                    if (_activeAlarms.Add(e.AlarmId)) Log(ms, "alarm", $"{e.AlarmId} {e.Level} {e.Value?.ToString("0.#", CultureInfo.InvariantCulture)}".TrimEnd());
                    break;
                case AlarmCleared e:
                    if (_activeAlarms.Remove(e.AlarmId)) Log(ms, "alarm", $"{e.AlarmId} cleared");
                    break;
                case CrewHealthBandChanged e:
                    Log(ms, "crew", $"{e.Crew} {e.Previous} -> {e.Current} ({e.Health:0})");
                    break;
                case CrewMemberDied e:
                    Key("crew_died", e.Crew.Value, ms);
                    Log(ms, "crew", $"{e.Crew} DIED ({e.PrimaryCause})");
                    break;
                case MissionEnded e:
                    var outcome = e.Outcome == MissionOutcomeKind.Recovered ? "recovered" : "all_crew_lost";
                    Key("ended", outcome, ms);
                    Log(ms, "end", $"{outcome} ({e.EndConditionId})");
                    break;
                case DockingDamaged e:
                    Log(ms, "vehicle", $"{e.Port} damaged");
                    break;
                case SessionEffectRefused e:
                    Log(ms, "refused", $"effect {e.Target}: {e.ReasonCode}");
                    break;
                case ChuteDeployed e:
                    Log(ms, "flight", $"{e.Chute} chute {e.Result} at {e.AltitudeM:0} m");
                    break;
                case BurnStarted e:
                    Log(ms, "flight", $"{e.VehicleId} burn started");
                    break;
                case BurnEnded e:
                    Log(ms, "flight", $"{e.VehicleId} burn ended, dv {e.DeltaVMps:0.0} m/s");
                    break;
                case Splashdown e:
                    Log(ms, "flight", $"splashdown {e.ZoneId ?? "outside zones"} lon {e.LandingLongitudeDeg:0.0}, wait {e.RecoveryWaitHours:0.#} h");
                    break;
            }
        }

        private void Sample()
        {
            var state = _session.State;
            var health = new double[_crew.Length];
            for (var i = 0; i < _crew.Length; i++) health[i] = state.GetNumber($"crew.{_crew[i].Value}.health");
            var values = new double[_extraSamples.Count];
            for (var i = 0; i < values.Length; i++) values[i] = Number(state, _extraSamples[i].Path);
            _samples.Add(new ScenarioSample(
                _session.Now.Milliseconds,
                _parts.Director.Phase,
                state.GetBool("flight.docked"),
                Number(state, _samplePaths[0]),
                Number(state, _samplePaths[1]),
                Number(state, _samplePaths[2]),
                Number(state, _samplePaths[3]),
                Number(state, _samplePaths[4]),
                Number(state, _samplePaths[5]),
                values,
                health));
        }

        private SortedDictionary<string, string> Variants()
        {
            var variants = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var variant in _parts.Data.Scenario.Incidents.Variants) variants[variant.Id] = _session.State.GetText($"variant.{variant.Id}");
            return variants;
        }

        private ScenarioMetrics Metrics()
        {
            long? i1 = _incidentKey is not null && _keys.TryGetValue(_incidentKey, out var incidentMs) ? incidentMs : null;
            double? incident = i1 / 1000.0;
            long? undockMs = null;
            for (var i = 1; i < _samples.Count && undockMs is null; i++)
            {
                if (_samples[i - 1].Docked && !_samples[i].Docked) undockMs = _samples[i].MetMs;
            }
            double? Reach(long? from, double dps)
            {
                if (from is not { } fromMs) return null;
                foreach (var s in _samples)
                {
                    if (s.MetMs >= fromMs && Math.Abs(s.RollDps) >= dps) return (s.MetMs - fromMs) / 1000.0;
                }
                return null;
            }

            var maxRoll = 0.0;
            var atIncident = _samples[0].Values;
            var minHealth = new double[_crew.Length];
            Array.Fill(minHealth, double.MaxValue);
            foreach (var s in _samples)
            {
                maxRoll = Math.Max(maxRoll, Math.Abs(s.RollDps));
                if (i1 is { } incidentAt && s.MetMs <= incidentAt) atIncident = s.Values;
                for (var c = 0; c < minHealth.Length; c++) minHealth[c] = Math.Min(minHealth[c], s.CrewHealth[c]);
            }
            var last = _samples[^1];
            var zone = _session.State.GetText("flight.zone");
            return new ScenarioMetrics(
                incident, Reach(i1, 20), Reach(i1, 120), Reach(undockMs, 120), maxRoll, atIncident, last.Values, minHealth,
                _parts.Tracker.EntryPeakG, zone.Length == 0 ? "-" : zone, (_session.Now.Milliseconds - _samples[0].MetMs) / 1000.0, _estimateZone);
        }

        private IExpression? Compile(string source, string what)
        {
            try
            {
                return _parts.Compiler.Compile(source);
            }
            catch (ExpressionException ex)
            {
                _failures.Add($"{what}: {ex.Message}");
                return null;
            }
        }

        private bool Holds(IExpression expression, string what)
        {
            try
            {
                return expression.Evaluate(_session.State).AsBool();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnknownStatePathException or ExpressionException)
            {
                _failures.Add($"{what}: {ex.Message}");
                return false;
            }
        }

        private void Key(string kind, string subject, long ms) => _keys.TryAdd(ScenarioEventKeys.Of(kind, subject), ms);

        private void Log(long ms, string kind, string text) => _timeline.Add(new TimelineEntry(ms, kind, text));

        private static double Number(IStateQuery state, string path) => state.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? value.AsNumber() : double.NaN;

        private static long CeilSecond(long ms) => (ms + SecondMs - 1) / SecondMs * SecondMs;

        private static string Describe(CommandRequest request)
        {
            var args = string.Join(" ", request.Args.Select(a => $"{a.Key}={a.Value}"));
            var who = request.Assignee is { } crew ? $" ({crew})" : "";
            return $"{request.Action}{(args.Length > 0 ? " " + args : "")}{who}";
        }
    }

    /// <summary>MET as hh:mm:ss.</summary>
    public static string Met(long ms)
    {
        var total = ms / 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}");
    }
}
