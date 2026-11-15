using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Knowledge;

/// <summary>
/// Everything the player and crew have earned: per-fact observation history, current knowledge state,
/// per-crew known views, ground bands and per-component knowledge. It never reads truth; it changes only
/// through validated observations, suspicions, tells and repairs, all kept in an append-only journal.
/// </summary>
/// <remarks>
/// State paths: <c>knowledge.fact.&lt;factId&gt;.state|value|band|met_s</c> and
/// <c>knowledge.component.&lt;componentId&gt;.state|stale|condition|variant|repair_unverified</c>.
/// A value that is not known yet reads as <see cref="StateValue.None"/>; a missing band or condition reads as ''.
/// </remarks>
public sealed class KnowledgeBase : ISimSystem, IStateProvider, IHashContributor
{
    private const string FactPrefix = StatePaths.KnowledgeFact;
    private const string ComponentPrefix = StatePaths.KnowledgeComponent;

    private static readonly int MethodCount = Enum.GetValues<ObservationMethod>().Length;
    private static readonly StateValue EmptyText = StateValue.Of("");
    private static readonly StateValue[] FactStateWords =
        [StateValue.Of("unknown"), StateValue.Of("known"), StateValue.Of("stale"), StateValue.Of("conflicting"), StateValue.Of("estimated")];
    private static readonly StateValue[] ComponentStateWords =
        [StateValue.Of("unknown"), StateValue.Of("suspected"), StateValue.Of("confirmed_nominal"), StateValue.Of("confirmed_damaged")];

    private readonly FactCatalog _catalog;
    private readonly IObservationAccess _access;
    private readonly EventBus _events;
    private readonly RevealableComponents _revealable;

    private readonly Dictionary<string, FactRecord> _facts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FactRecord>.AlternateLookup<ReadOnlySpan<char>> _factsSpan;
    private readonly List<FactRecord> _factOrder = [];

    private readonly Dictionary<string, ComponentRecord> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentRecord>.AlternateLookup<ReadOnlySpan<char>> _componentsSpan;
    private readonly List<ComponentRecord> _componentOrder = [];

    private readonly Dictionary<CrewId, Dictionary<FactId, Observation>> _crewViews = [];
    private readonly List<Observation> _observations = [];
    private readonly List<KnowledgeEntry> _journal = [];

    /// <param name="revealableComponents">When given, only these components can be suspected or confirmed and served as state.</param>
    public KnowledgeBase(FactCatalog catalog, IObservationAccess access, EventBus events, IEnumerable<ComponentId>? revealableComponents = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _revealable = new RevealableComponents(revealableComponents);
        Validator = new ObservationValidator(catalog, access, _revealable);
        _factsSpan = _facts.GetAlternateLookup<ReadOnlySpan<char>>();
        _componentsSpan = _components.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Staleness is re-evaluated once per second (SCRIPTING 6.2).</summary>
    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public string Prefix => "knowledge.";

    public ObservationValidator Validator { get; }

    /// <summary>
    /// The MET staleness is judged at. Only <see cref="Step"/> and <see cref="Restore"/> move it, so an observation
    /// stamped in the future can never push the clock ahead (it simply stays fresh until the clock catches up).
    /// </summary>
    public MissionTime Now { get; private set; }

    /// <summary>Append-only journal; save it and replay it with <see cref="Restore"/>.</summary>
    public IReadOnlyList<KnowledgeEntry> Journal => _journal;

    /// <summary>Every accepted observation in id order (for the debrief).</summary>
    public IReadOnlyList<Observation> Observations => _observations;

    /// <summary>Validates and records an observation; publishes the resulting events.</summary>
    public ObservationResult Submit(ObservationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!_catalog.TryResolve(report.Fact, out var fact))
            return new ObservationResult(new ObservationVerdict(ObservationRejection.UnknownFact, $"No fact '{report.Fact}'."), null);

        var verdict = Validator.Validate(report, fact);
        if (!verdict.Accepted) return new ObservationResult(verdict, null);

        var observation = report.ToObservation(new ObservationId(_observations.Count + 1));
        Apply(observation, fact, publish: true);
        return new ObservationResult(verdict, observation);
    }

    /// <summary>
    /// Feeds a telemetry sample for a downlink fact. Only a change of band word is recorded, as a
    /// downlink observation carrying the word and never the value.
    /// </summary>
    /// <returns>True when the band changed.</returns>
    public bool ReportTelemetry(FactDefinition fact, StateValue telemetry, MissionTime time)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.Downlink is not { } downlink) throw new ArgumentException($"Fact '{fact.Id}' has no downlink.", nameof(fact));
        if (downlink.WordFor(telemetry) is not { } word) return false;
        if (_facts.TryGetValue(fact.Id.Value, out var record) &&
            string.Equals(record.LatestBand?.Qualitative, word, StringComparison.Ordinal))
            return false;

        return Submit(new ObservationReport(fact.Id, null, word, null, ObservationMethod.Downlink, 1.0, time, [])).Accepted;
    }

    /// <summary>
    /// Records one precise reading from a ground telemetry dump (unlike <see cref="ReportTelemetry"/>, which only tracks bands).
    /// A number fact takes a number; a text fact takes text. The fact must allow the telemetry method.
    /// A telemetry reading has no observer, so no crew member knows it until it is relayed: an accepted reading is told
    /// (journalled <see cref="Tell"/>) to every living member of <paramref name="relayTo"/>.
    /// </summary>
    /// <param name="relayTo">Crew who hear the reading read back over the radio; empty (the default) keeps it on the ground.</param>
    public ObservationResult ReportTelemetryReading(FactDefinition fact, StateValue reading, MissionTime time, IReadOnlyList<CrewId>? relayTo = null)
    {
        ArgumentNullException.ThrowIfNull(fact);
        var report = fact.Kind == FactValueKind.Number
            ? ObservationReport.Telemetry(fact.Id, reading.Kind == StateValueKind.Number ? reading.AsNumber() : null, null, time)
            : ObservationReport.Telemetry(fact.Id, null, reading.Kind == StateValueKind.Text ? reading.AsText() : null, time);
        var result = Submit(report);
        if (result.Observation is { } observation && relayTo is not null)
        {
            for (var i = 0; i < relayTo.Count; i++) Tell(relayTo[i], observation.Id, time);
        }
        return result;
    }

    /// <summary>Shares an existing observation with <paramref name="listener"/> (radio relay, crew talking).</summary>
    /// <returns>False when the observation does not exist or the listener is not alive.</returns>
    public bool Tell(CrewId listener, ObservationId observation, MissionTime time)
    {
        if (observation.Value < 1 || observation.Value > _observations.Count || !_access.IsAlive(listener)) return false;
        ApplyTell(new TellEntry(time, listener, observation));
        return true;
    }

    /// <summary>Marks a component suspected because of a clue. A clue never confirms anything.</summary>
    /// <returns>True when the component's state changed (it was unknown).</returns>
    public bool Suspect(ComponentId component, string reason, MissionTime time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return ApplySuspicion(new SuspicionEntry(time, component, reason, Raised: true), publish: true);
    }

    /// <summary>Withdraws a clue; a suspected component with no clues left returns to unknown. A no-op for a clue never raised.</summary>
    public bool ClearSuspicion(ComponentId component, string reason, MissionTime time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return ApplySuspicion(new SuspicionEntry(time, component, reason, Raised: false), publish: true);
    }

    /// <summary>
    /// Notes that a repair was installed on <paramref name="component"/>. Knowledge keeps the last confirmed
    /// condition, flagged unverified, until a later inspection or test confirms it again.
    /// </summary>
    public void RecordRepair(ComponentId component, string repairId, MissionTime time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repairId);
        ApplyRepair(new RepairEntry(time, component, repairId), publish: true);
    }

    /// <summary>Rebuilds state from a saved journal into this empty knowledge base, without publishing events.</summary>
    /// <exception cref="InvalidOperationException">The knowledge base is not empty or the journal is inconsistent.</exception>
    public void Restore(IReadOnlyList<KnowledgeEntry> journal, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (_journal.Count > 0) throw new InvalidOperationException("Restore needs an empty knowledge base.");

        foreach (var entry in journal)
        {
            switch (entry)
            {
                case ObservationEntry { Observation: var observation }:
                    if (observation.Id.Value != _observations.Count + 1)
                        throw new InvalidOperationException($"Journal observation {observation.Id} is out of sequence.");
                    if (!_catalog.TryResolve(observation.Fact, out var fact))
                        throw new InvalidOperationException($"Journal names unknown fact '{observation.Fact}'.");
                    Apply(observation, fact, publish: false);
                    break;
                case TellEntry tell:
                    if (tell.Observation.Value < 1 || tell.Observation.Value > _observations.Count)
                        throw new InvalidOperationException($"Journal tells unknown observation {tell.Observation}.");
                    ApplyTell(tell);
                    break;
                case SuspicionEntry suspicion:
                    ApplySuspicion(suspicion, publish: false);
                    break;
                case RepairEntry repair:
                    ApplyRepair(repair, publish: false);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported journal entry {entry.GetType().Name}.");
            }
        }

        Advance(now);
        foreach (var record in _factOrder) record.State = Evaluate(record);
        foreach (var record in _componentOrder) record.IsStale = IsComponentStale(record);
    }

    public void Step(in StepContext context)
    {
        Advance(context.Now);
        for (var i = 0; i < _factOrder.Count; i++) Refresh(_factOrder[i], Now);
        for (var i = 0; i < _componentOrder.Count; i++)
        {
            var record = _componentOrder[i];
            var stale = IsComponentStale(record);
            if (stale == record.IsStale) continue;
            record.IsStale = stale;
            _events.Publish(new ComponentKnowledgeChanged(Now, record.Component, record.State, record.Snapshot()));
        }
    }

    public KnowledgeState StateOf(FactId fact)
    {
        if (_facts.TryGetValue(fact.Value, out var record)) return record.State;
        return _catalog.IsKnownFact(fact.Value) ? KnowledgeState.Unknown : throw new KeyNotFoundException($"Unknown fact '{fact}'.");
    }

    public FactKnowledge Describe(FactId fact)
    {
        if (!_facts.TryGetValue(fact.Value, out var record))
        {
            return _catalog.IsKnownFact(fact.Value)
                ? new FactKnowledge(fact, KnowledgeState.Unknown, null, null, [], null)
                : throw new KeyNotFoundException($"Unknown fact '{fact}'.");
        }

        var conflicts = new List<Observation>();
        if (record.LatestMeasurement is { } latest && IsFresh(record.Definition, latest)) CollectConflicts(record, conflicts);
        return new FactKnowledge(fact, record.State, record.LatestMeasurement, record.LatestEstimate, conflicts, record.LatestBand);
    }

    /// <summary>Every observation of <paramref name="fact"/> in record order, including ground bands and estimates.</summary>
    public IReadOnlyList<Observation> HistoryOf(FactId fact) =>
        _facts.TryGetValue(fact.Value, out var record) ? record.History.AsReadOnly() : [];

    /// <summary>The latest downlinked band word, or null before the first sample.</summary>
    public string? GroundBandOf(FactId fact) =>
        _facts.TryGetValue(fact.Value, out var record) ? record.LatestBand?.Qualitative : null;

    public ComponentKnowledge ComponentOf(ComponentId component) =>
        _components.TryGetValue(component.Value, out var record) ? record.Snapshot() : ComponentKnowledge.Unknown(component);

    /// <summary>Every component with any knowledge (suspected or confirmed, or touched by a repair), sorted by id.</summary>
    public IReadOnlyList<ComponentKnowledge> KnownComponents()
    {
        var result = new ComponentKnowledge[_componentOrder.Count];
        for (var i = 0; i < result.Length; i++) result[i] = _componentOrder[i].Snapshot();
        return result;
    }

    /// <summary>The latest observation of <paramref name="fact"/> that <paramref name="crew"/> made or was told.</summary>
    public Observation? LatestKnownBy(CrewId crew, FactId fact) =>
        _crewViews.TryGetValue(crew, out var view) && view.TryGetValue(fact, out var observation) ? observation : null;

    public bool Knows(CrewId crew, FactId fact) => LatestKnownBy(crew, fact) is not null;

    /// <summary>Facts <paramref name="crew"/> can talk about, sorted by id.</summary>
    public IReadOnlyList<FactId> FactsKnownBy(CrewId crew)
    {
        if (!_crewViews.TryGetValue(crew, out var view)) return [];
        var facts = view.Keys.ToArray();
        Array.Sort(facts);
        return facts;
    }

    public bool TryGet(string path, out StateValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        var span = path.AsSpan();
        if (span.StartsWith(FactPrefix, StringComparison.Ordinal)) return TryGetFact(span[FactPrefix.Length..], out value);
        if (span.StartsWith(ComponentPrefix, StringComparison.Ordinal)) return TryGetComponent(span[ComponentPrefix.Length..], out value);
        value = StateValue.None;
        return false;
    }

    public bool IsKnownPath(string path) => TryGet(path, out _);

    private bool TryGetFact(ReadOnlySpan<char> rest, out StateValue value)
    {
        value = StateValue.None;
        var dot = rest.LastIndexOf('.');
        if (dot <= 0) return false;
        var id = rest[..dot];
        if (!_factsSpan.TryGetValue(id, out var record))
        {
            record = null;
            if (!_catalog.IsKnownFact(id)) return false;
        }

        switch (rest[(dot + 1)..])
        {
            case "state":
                value = FactStateWords[(int)(record?.State ?? KnowledgeState.Unknown)];
                return true;
            case "value":
                value = record is null ? StateValue.None : ValueOf(record);
                return true;
            case "band":
                value = record?.LatestBand?.Qualitative is { } word ? StateValue.Of(word) : EmptyText;
                return true;
            case "met_s":
                value = record is not null && ValueSource(record) is { } source ? StateValue.Of(source.Time.Seconds) : StateValue.Of(-1.0);
                return true;
            default:
                return false;
        }
    }

    private bool TryGetComponent(ReadOnlySpan<char> rest, out StateValue value)
    {
        value = StateValue.None;
        var dot = rest.LastIndexOf('.');
        if (dot <= 0) return false;
        var id = rest[..dot];
        if (!_componentsSpan.TryGetValue(id, out var record))
        {
            record = null;
            if (!_revealable.Contains(id)) return false;
        }

        switch (rest[(dot + 1)..])
        {
            case "state":
                value = ComponentStateWords[(int)(record?.State ?? ComponentKnowledgeState.Unknown)];
                return true;
            case "stale":
                value = StateValue.Of(record?.IsStale ?? false);
                return true;
            case "condition":
                value = record?.Condition is { } condition ? StateValue.Of(condition) : EmptyText;
                return true;
            case "variant":
                value = record?.Variant is { } variant ? StateValue.Of(variant) : EmptyText;
                return true;
            case "repair_unverified":
                value = StateValue.Of(record?.RepairUnverified ?? false);
                return true;
            default:
                return false;
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("knowledge");
        writer.Write((long)_journal.Count);
        foreach (var entry in _journal)
        {
            writer.Write(entry.Time.Milliseconds);
            switch (entry)
            {
                case ObservationEntry { Observation: var o }:
                    writer.Write("observation");
                    writer.Write(o.Id.Value);
                    writer.Write(o.Fact.Value);
                    writer.Write(o.Value.HasValue);
                    writer.Write(o.Value ?? 0.0);
                    writer.Write(o.Qualitative is not null);
                    writer.Write(o.Qualitative ?? "");
                    writer.Write(o.Observer?.Value ?? "");
                    writer.Write((long)o.Method);
                    writer.Write(o.Confidence);
                    writer.Write(o.Time.Milliseconds);
                    writer.Write((long)o.Scope.Count);
                    foreach (var component in o.Scope) writer.Write(component.Value);
                    writer.Write(o.Origin?.Value ?? "");
                    break;
                case TellEntry tell:
                    writer.Write("tell");
                    writer.Write(tell.Listener.Value);
                    writer.Write(tell.Observation.Value);
                    break;
                case SuspicionEntry suspicion:
                    writer.Write("suspicion");
                    writer.Write(suspicion.Component.Value);
                    writer.Write(suspicion.Reason);
                    writer.Write(suspicion.Raised);
                    break;
                case RepairEntry repair:
                    writer.Write("repair");
                    writer.Write(repair.Component.Value);
                    writer.Write(repair.RepairId);
                    break;
            }
        }
    }

    private void Apply(Observation observation, FactDefinition fact, bool publish)
    {
        _journal.Add(new ObservationEntry(observation.Time, observation));
        _observations.Add(observation);

        var record = GetOrCreateFact(fact);
        record.History.Add(observation);
        var capabilities = _catalog.Capabilities(observation.Method);
        var previousBand = record.LatestBand?.Qualitative;
        var previousState = record.State;
        var previousSource = ValueSource(record);

        switch (capabilities.Yield)
        {
            case ObservationYield.Measurement:
                var method = (int)observation.Method;
                if (IsNewer(observation, record.LatestByMethod[method])) record.LatestByMethod[method] = observation;
                if (IsNewer(observation, record.LatestMeasurement)) record.LatestMeasurement = observation;
                break;
            case ObservationYield.Estimate:
                if (IsNewer(observation, record.LatestEstimate)) record.LatestEstimate = observation;
                break;
            case ObservationYield.Band:
                if (IsNewer(observation, record.LatestBand)) record.LatestBand = observation;
                break;
        }

        if (observation.Observer is { } observer) NoteForCrew(observer, observation);
        if (publish) _events.Publish(new ObservationRecorded(observation.Time, observation));

        if (capabilities.Yield == ObservationYield.Band)
        {
            var band = record.LatestBand!.Qualitative!;
            if (publish && !string.Equals(previousBand, band, StringComparison.Ordinal))
                _events.Publish(new GroundBandChanged(observation.Time, fact.Id, previousBand, band));
        }
        else if (publish)
        {
            var state = Evaluate(record);
            record.State = state;
            if (state != previousState || !ReferenceEquals(ValueSource(record), previousSource))
                _events.Publish(new KnowledgeChanged(observation.Time, fact.Id, previousState, state, record.LatestMeasurement?.Id));
            for (var i = 0; i < record.ConflictGroup.Count; i++)
            {
                var peerId = record.ConflictGroup[i];
                if (peerId != fact.Id && _facts.TryGetValue(peerId.Value, out var peer)) Refresh(peer, observation.Time);
            }
        }

        if (fact.ConfirmsComponent is { } component && capabilities.CanConfirmComponent)
            Confirm(component, fact, observation, publish);
    }

    private void Confirm(ComponentId component, FactDefinition conditionFact, Observation observation, bool publish)
    {
        var record = GetOrCreateComponent(component, conditionFact);
        if (!IsNewer(observation, record.ConfirmedBy)) return;

        var previous = record.State;
        record.ConfirmedBy = observation;
        var text = observation.Qualitative!;
        if (conditionFact.IsNominalText(text))
        {
            record.State = ComponentKnowledgeState.ConfirmedNominal;
            record.Condition = text;
            record.Variant = null;
        }
        else
        {
            record.State = ComponentKnowledgeState.ConfirmedDamaged;
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            record.Condition = colon < 0 ? text : text[..colon];
            record.Variant = colon < 0 ? null : text[(colon + 1)..];
        }
        if (record.RepairTime is { } repairedAt && observation.Time >= repairedAt)
        {
            record.RepairUnverified = false;
            record.RepairTime = null;
        }
        record.IsStale = IsComponentStale(record);
        if (publish) _events.Publish(new ComponentKnowledgeChanged(observation.Time, component, previous, record.Snapshot()));
    }

    private void ApplyTell(TellEntry tell)
    {
        _journal.Add(tell);
        NoteForCrew(tell.Listener, _observations[(int)tell.Observation.Value - 1]);
    }

    /// <summary>
    /// Records a raised or withdrawn clue. Nothing is journalled when the clue list would not change
    /// (a repeated clue, or withdrawing one that was never raised, including on a component never seen).
    /// Publishes whenever the clue list changes, even when the state stays the same.
    /// </summary>
    /// <returns>True when the component's state changed.</returns>
    private bool ApplySuspicion(SuspicionEntry entry, bool publish)
    {
        ComponentRecord record;
        if (entry.Raised)
        {
            record = GetOrCreateComponent(entry.Component, null);
            if (record.Suspicions.Contains(entry.Reason)) return false;
        }
        else if (!_components.TryGetValue(entry.Component.Value, out record!) || !record.Suspicions.Contains(entry.Reason))
        {
            return false;
        }
        _journal.Add(entry);

        var previous = record.State;
        if (entry.Raised)
        {
            record.Suspicions.Add(entry.Reason);
            if (record.State == ComponentKnowledgeState.Unknown) record.State = ComponentKnowledgeState.Suspected;
        }
        else
        {
            record.Suspicions.Remove(entry.Reason);
            if (record.State == ComponentKnowledgeState.Suspected && record.Suspicions.Count == 0) record.State = ComponentKnowledgeState.Unknown;
        }

        if (publish) _events.Publish(new ComponentKnowledgeChanged(entry.Time, entry.Component, previous, record.Snapshot()));
        return previous != record.State;
    }

    private void ApplyRepair(RepairEntry entry, bool publish)
    {
        var record = GetOrCreateComponent(entry.Component, null);
        _journal.Add(entry);
        record.RepairUnverified = true;
        record.RepairTime = entry.Time;
        if (publish) _events.Publish(new ComponentKnowledgeChanged(entry.Time, entry.Component, record.State, record.Snapshot()));
    }

    private void Refresh(FactRecord record, MissionTime time)
    {
        var state = Evaluate(record);
        if (state == record.State) return;
        var previous = record.State;
        record.State = state;
        _events.Publish(new KnowledgeChanged(time, record.Definition.Id, previous, state, record.LatestMeasurement?.Id));
    }

    private KnowledgeState Evaluate(FactRecord record)
    {
        var fact = record.Definition;
        if (record.LatestMeasurement is { } measured && IsFresh(fact, measured))
            return CollectConflicts(record, null) ? KnowledgeState.Conflicting : KnowledgeState.Known;
        if (record.LatestEstimate is { } estimate && IsFresh(fact, estimate)) return KnowledgeState.Estimated;
        return record.LatestMeasurement is not null || record.LatestEstimate is not null ? KnowledgeState.Stale : KnowledgeState.Unknown;
    }

    /// <summary>
    /// A conflict is two fresh values that differ beyond tolerance: the latest reading of each method for this
    /// fact, or this fact's latest reading against another fact in its conflict group (gauge vs portable check).
    /// </summary>
    private bool CollectConflicts(FactRecord record, List<Observation>? sink)
    {
        var fact = record.Definition;
        var latest = record.LatestMeasurement!;
        var found = false;

        var byMethod = record.LatestByMethod;
        for (var i = 0; i < byMethod.Length; i++)
        {
            if (byMethod[i] is not { } a || !IsFresh(fact, a)) continue;
            for (var j = i + 1; j < byMethod.Length; j++)
            {
                if (byMethod[j] is not { } b || !IsFresh(fact, b) || !Differ(fact.Kind, a, b, fact.ConflictTolerance)) continue;
                if (sink is null) return true;
                found = true;
                if (!ReferenceEquals(a, latest) && !sink.Contains(a)) sink.Add(a);
                if (!ReferenceEquals(b, latest) && !sink.Contains(b)) sink.Add(b);
            }
        }

        for (var i = 0; i < record.ConflictGroup.Count; i++)
        {
            var peerId = record.ConflictGroup[i];
            if (peerId == fact.Id || !_facts.TryGetValue(peerId.Value, out var peer)) continue;
            if (peer.LatestMeasurement is not { } other || !IsFresh(peer.Definition, other)) continue;
            var tolerance = Math.Max(fact.ConflictTolerance, peer.Definition.ConflictTolerance);
            if (!Differ(fact.Kind, latest, other, tolerance)) continue;
            if (sink is null) return true;
            found = true;
            sink.Add(other);
        }
        return found;
    }

    private static bool Differ(FactValueKind kind, Observation a, Observation b, double tolerance) => kind == FactValueKind.Number
        ? Math.Abs(a.Value!.Value - b.Value!.Value) > tolerance
        : !string.Equals(a.Qualitative, b.Qualitative, StringComparison.Ordinal);

    private bool IsFresh(FactDefinition fact, Observation observation) => !fact.IsStale(observation.Time, Now);

    private bool IsComponentStale(ComponentRecord record) =>
        record.ConfirmedBy is { } confirmed && record.ConditionFact.IsStale(confirmed.Time, Now);

    /// <summary>The observation <c>value</c> and <c>met_s</c> are served from: the estimate while Estimated, else the latest measurement.</summary>
    private static Observation? ValueSource(FactRecord record) =>
        record.State == KnowledgeState.Estimated ? record.LatestEstimate : record.LatestMeasurement ?? record.LatestEstimate;

    private static StateValue ValueOf(FactRecord record)
    {
        if (ValueSource(record) is not { } source) return StateValue.None;
        return record.Definition.Kind == FactValueKind.Number ? StateValue.Of(source.Value!.Value) : StateValue.Of(source.Qualitative!);
    }

    /// <summary>Later MET wins; at equal MET the later-recorded observation wins.</summary>
    private static bool IsNewer(Observation candidate, Observation? current) =>
        current is null || candidate.Time > current.Time || (candidate.Time == current.Time && candidate.Id.Value > current.Id.Value);

    private void NoteForCrew(CrewId crew, Observation observation)
    {
        if (!_crewViews.TryGetValue(crew, out var view))
        {
            view = [];
            _crewViews[crew] = view;
        }
        if (!view.TryGetValue(observation.Fact, out var current) || IsNewer(observation, current)) view[observation.Fact] = observation;
    }

    private void Advance(MissionTime time)
    {
        if (time > Now) Now = time;
    }

    private FactRecord GetOrCreateFact(FactDefinition fact)
    {
        if (_facts.TryGetValue(fact.Id.Value, out var record)) return record;
        record = new FactRecord(fact, _catalog.ConflictGroupOf(fact.Id), MethodCount);
        _facts.Add(fact.Id.Value, record);
        InsertSorted(_factOrder, record, static (a, b) => a.Definition.Id.CompareTo(b.Definition.Id));
        return record;
    }

    private ComponentRecord GetOrCreateComponent(ComponentId component, FactDefinition? conditionFact)
    {
        if (_components.TryGetValue(component.Value, out var record)) return record;
        if (!_revealable.Contains(component))
            throw new ArgumentException($"Component '{component}' is not revealable.", nameof(component));
        conditionFact ??= _catalog.Get(_catalog.ComponentConditionFact(component));
        record = new ComponentRecord(component, conditionFact);
        _components.Add(component.Value, record);
        InsertSorted(_componentOrder, record, static (a, b) => a.Component.CompareTo(b.Component));
        return record;
    }

    private static void InsertSorted<T>(List<T> list, T item, Comparison<T> comparison)
    {
        var index = list.Count;
        while (index > 0 && comparison(list[index - 1], item) > 0) index--;
        list.Insert(index, item);
    }
}
