using System.Globalization;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Scoring;

namespace Usa.Game.Content;

/// <summary>
/// Turns a <see cref="ScoreBreakdown"/> into a fully worded <see cref="DebriefView"/>. Never throws on missing
/// content: a gap gets the neutral text of <see cref="DebriefLabels"/> and is listed in <see cref="DebriefView.MissingKeys"/>.
/// </summary>
public sealed class DebriefComposer
{
    private readonly DebriefDefinition _debrief;
    private readonly Dictionary<string, string> _crewNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _objectiveTitles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageSlot> _images = new(StringComparer.Ordinal);
    private readonly DebriefMissionReferences? _mission;
    private readonly int _crewCount;

    /// <param name="debrief">A validated debrief.</param>
    /// <param name="briefing">The same mission's briefing: crew names, objective titles and image slots.</param>
    /// <param name="mission">
    /// The same mission's data: fact labels and component names for the timeline. Without it those names are reported missing
    /// and shown as a dash, never as raw ids.
    /// </param>
    public DebriefComposer(DebriefDefinition debrief, BriefingDefinition briefing, DebriefMissionReferences? mission = null)
    {
        ArgumentNullException.ThrowIfNull(debrief);
        ArgumentNullException.ThrowIfNull(briefing);
        _debrief = debrief;
        _mission = mission;
        foreach (var member in briefing.Crew) _crewNames.TryAdd(member.Id.Value, member.Name);
        foreach (var objective in briefing.Objectives) _objectiveTitles.TryAdd(objective.Id.Value, objective.Title);
        foreach (var slot in briefing.ImageSlots) _images.TryAdd(slot.Id, slot);
        _crewCount = briefing.Crew.Count;
    }

    /// <summary>How many known items a discovery or decision lists.</summary>
    public const int MaxKnownItems = 5;

    private DebriefLabels Labels => _debrief.Labels;

    public DebriefView Compose(ScoreBreakdown breakdown, DebriefFlightFacts facts)
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        ArgumentNullException.ThrowIfNull(facts);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var lostCrew = LostCrew(breakdown);

        var capNote = CapNote(breakdown, lostCrew.Count, missing);
        var dimensions = breakdown.Dimensions.Select(d => Dimension(d, facts, missing)).ToArray();
        var objectives = breakdown.Objectives.Select(o => Objective(o, facts, missing)).ToArray();
        var sPlus = SPlus(breakdown.SPlus, missing);
        var bestDecisions = breakdown.BestDecisions.Select(d => Decision(d, missing)).ToArray();
        var largestLoss = breakdown.LargestAvoidableLoss is { } loss ? Decision(loss, missing) : null;
        var historyCard = HistoryCard(missing);
        var comparison = Comparison(facts, lostCrew, missing);
        var crewConditions = CrewConditions(facts, missing);
        var timeline = Timeline(facts, missing);

        return new DebriefView(
            breakdown.Grade,
            breakdown.GradeLabel,
            breakdown.ScorePoints,
            breakdown.RawPoints,
            breakdown.FinalPoints,
            breakdown.CapPoints,
            capNote,
            dimensions,
            objectives,
            sPlus,
            bestDecisions,
            largestLoss,
            historyCard,
            comparison,
            _debrief.Labels,
            _debrief.FictionalAdaptationText,
            missing.ToArray())
        {
            CrewConditions = crewConditions,
            Timeline = timeline,
            CrewLosses = CrewLosses(facts, missing),
            Splashdown = Splashdown(facts, missing),
        };
    }

    private string? CapNote(ScoreBreakdown breakdown, int lost, SortedSet<string> missing)
    {
        if (lost == 0) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cap"] = ContentFormat.WholeNumber(breakdown.CapPoints),
            ["lost"] = lost.ToString(CultureInfo.InvariantCulture),
            ["crewCount"] = Math.Max(_crewCount, lost).ToString(CultureInfo.InvariantCulture),
        };
        return TextTemplate.Fill(_debrief.Labels.CapNote, values, name => missing.Add($"labels.capNote{{{name}}}"));
    }

    private DebriefDimensionView Dimension(DimensionScore score, DebriefFlightFacts facts, SortedSet<string> missing)
    {
        var name = Lookup(_debrief.Dimensions, "dimensions", ContentChecks.SnakeName(score.Dimension), missing);
        var notLanded = score.Notes.Any(note => note.Key == ScoringExplanationKeys.NotLandedNote);
        var lines = score.Lines
            .Select(line => new DebriefScoreLineView(line.Key, line.Subject, LineText(line, notLanded, facts, missing), line.Points, line.MaxPoints))
            .ToArray();
        var notes = score.Notes.Select(note => Note(note, missing)).ToArray();
        return new DebriefDimensionView(score.Dimension, name, score.Points, score.WeightPoints, lines, notes);
    }

    private string LineText(ScoreLine line, bool notLanded, DebriefFlightFacts facts, SortedSet<string> missing)
    {
        if (line.Key != ScoringExplanationKeys.RecoveryZoneLine)
            return Explain(line.Key, SubjectOf(line.Subject, missing), null, line.Points, line.MaxPoints, Labels.NeutralScoreLine, missing);

        // The scorer still emits the zone line when the capsule never came down; naming a landing place would be false.
        if (notLanded)
            return Explain(ScoringExplanationKeys.NotLandedNote, () => null, null, line.Points, line.MaxPoints, Labels.NeutralScoreLine, missing);

        // The scorer's subject decides inside or outside the zones; the facts only supply the zone's display name.
        Func<string?> zone = line.Subject is not { } zoneId
            ? () => Labels.OpenOcean
            : () => facts.LandingZoneName ?? SubjectName(zoneId, missing);
        return Explain(line.Key, zone, null, line.Points, line.MaxPoints, Labels.NeutralScoreLine, missing);
    }

    private DebriefNoteView Note(ScoreNote note, SortedSet<string> missing) =>
        new(note.Key, note.Subject, note.Value, Explain(note.Key, SubjectOf(note.Subject, missing), note.Value, null, null, Labels.NeutralText, missing));

    private DebriefObjectiveView Objective(ObjectiveResult result, DebriefFlightFacts facts, SortedSet<string> missing)
    {
        var id = result.Id.Value;
        var title = SubjectName(id, missing);
        var stateText = Lookup(_debrief.ObjectiveStates, "objectiveStates", ContentChecks.SnakeName(result.FinalState), missing);
        var explanation = Explain(ObjectiveKey(result), SubjectOf(id, missing), null, result.Points, result.MaxPoints, Labels.NeutralScoreLine, missing);

        var lost = result.FinalState != ObjectiveFinalState.Completed;
        string? causeText = null;
        if (lost && result.Cause is { } cause)
            causeText = Lookup(_debrief.LossCauses, "lossCauses", ContentChecks.SnakeName(cause), missing);

        string? reasonKey = null;
        string? reasonText = null;
        if (lost && facts.ObjectiveReasonKeys is { } reasons && reasons.TryGetValue(result.Id, out var key))
        {
            reasonKey = key;
            reasonText = Lookup(_debrief.FeasibilityReasons, "feasibilityReasons", key, missing, Labels.NeutralText);
        }

        return new DebriefObjectiveView(
            result.Id, title, result.FinalState, stateText, explanation, lost ? result.Cause : null, causeText, reasonKey, reasonText,
            result.CountedInDenominator, result.Points, result.MaxPoints, result.FinalMetS,
            result.FinalMetS is { } met ? ContentFormat.Met(met) : null);
    }

    /// <summary>Mirrors the objectives dimension's key for this objective.</summary>
    private static string ObjectiveKey(ObjectiveResult result) => result.FinalState switch
    {
        ObjectiveFinalState.Completed => "objectives.completed",
        ObjectiveFinalState.Abandoned => "objectives.abandoned",
        _ when result.Cause == ObjectiveLossCause.PlayerAvoidable => "objectives.lost_avoidable",
        _ => "objectives.excluded_scenario",
    };

    private DebriefSPlusView SPlus(SPlusResult result, SortedSet<string> missing) =>
        new(result.Achieved, result.Path, _debrief.Labels.SplusTitle, result.Reasons.Select(r => Note(r, missing)).ToArray());

    private DebriefDecisionView Decision(DebriefItem item, SortedSet<string> missing) =>
        new(item.Key, item.Subject, Explain(item.Key, SubjectOf(item.Subject, missing), item.Magnitude, null, null, Labels.NeutralText, missing),
            item.Magnitude, item.MetS, item.MetS is { } met ? ContentFormat.Met(met) : null);

    private DebriefCrewConditionView[] CrewConditions(DebriefFlightFacts facts, SortedSet<string> missing)
    {
        if (facts.CrewConditions is not { Count: > 0 } conditions) return [];
        return conditions
            .Select(c => new DebriefCrewConditionView(
                c.Crew, SubjectName(c.Crew.Value, missing), c.LaunchHealth, c.WorstHealth, c.RecoveryHealth,
                ContentFormat.WholeNumber(c.LaunchHealth), ContentFormat.WholeNumber(c.WorstHealth),
                c.RecoveryHealth is { } recovery ? ContentFormat.WholeNumber(recovery) : TextTemplate.MissingValue)
            {
                LostMetText = c.RecoveryHealth is null && LossOf(facts, c.Crew) is { } loss ? ContentFormat.Met(loss.MetS) : null,
            })
            .ToArray();
    }

    private static DebriefCrewLoss? LossOf(DebriefFlightFacts facts, CrewId crew) =>
        facts.CrewLosses?.FirstOrDefault(loss => loss.Crew == crew);

    private DebriefCrewLossView[] CrewLosses(DebriefFlightFacts facts, SortedSet<string> missing) =>
        facts.CrewLosses is not { Count: > 0 } losses
            ? []
            : losses
                .OrderBy(loss => loss.MetS)
                .Select(loss => new DebriefCrewLossView(loss.Crew, SubjectName(loss.Crew.Value, missing), loss.MetS, ContentFormat.Met(loss.MetS)))
                .ToArray();

    /// <summary>From the facts alone: the zone the capsule came down in names it, no zone reads as the open ocean.</summary>
    private DebriefSplashdownView? Splashdown(DebriefFlightFacts facts, SortedSet<string> missing)
    {
        if (facts.SplashdownMetS is not { } metS) return null;
        var inZone = facts.LandingZoneId is not null || facts.LandingZoneName is not null;
        var zone = facts.LandingZoneName ?? (facts.LandingZoneId is { } zoneId ? SubjectName(zoneId, missing) : Labels.OpenOcean);
        return new DebriefSplashdownView(metS, ContentFormat.Met(metS), facts.LandingZoneId, zone, inZone);
    }

    /// <summary>
    /// The facts' incidents and decisions plus each incident's discovery: the first knowledge after it that
    /// <see cref="DebriefDefinition.IncidentEvidence"/> relates to it (an explicit discovery mark in the facts wins).
    /// Discoveries and decisions list what the ground knew then.
    /// </summary>
    private DebriefTimelineEntryView[] Timeline(DebriefFlightFacts facts, SortedSet<string> missing)
    {
        var knowledge = facts.Knowledge ?? [];
        var events = new List<DebriefTimelineEvent>(facts.Timeline ?? []);
        events.AddRange(Discoveries(events, knowledge, missing));
        return events
            .Select((e, order) => (Event: e, Order: order))
            .OrderBy(x => x.Event.MetS)
            .ThenBy(x => x.Event.Kind)
            .ThenBy(x => x.Order)
            .Select(x => new DebriefTimelineEntryView(
                x.Event.Kind,
                Lookup(_debrief.TimelineKinds, "timelineKinds", ContentChecks.SnakeName(x.Event.Kind), missing),
                x.Event.Key,
                TimelineText(x.Event, missing),
                x.Event.MetS,
                ContentFormat.Met(x.Event.MetS),
                x.Event.Kind == DebriefTimelineKind.Incident ? [] : KnownAt(x.Event.MetS, knowledge, missing)))
            .ToArray();
    }

    private List<DebriefTimelineEvent> Discoveries(List<DebriefTimelineEvent> events, IReadOnlyList<DebriefKnowledgeItem> knowledge, SortedSet<string> missing)
    {
        var marked = events.Where(e => e.Kind == DebriefTimelineKind.Discovery).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var discoveries = new List<DebriefTimelineEvent>();
        foreach (var incident in events)
        {
            if (incident.Kind != DebriefTimelineKind.Incident || !incident.Key.StartsWith(DebriefTimelineKeys.IncidentPrefix, StringComparison.Ordinal)) continue;
            var id = incident.Key[DebriefTimelineKeys.IncidentPrefix.Length..];
            var key = DebriefTimelineKeys.DiscoveryPrefix + id;
            if (!marked.Add(key)) continue;
            if (!_debrief.IncidentEvidence.TryGetValue(id, out var evidence))
            {
                missing.Add("incidentEvidence." + id);
                continue;
            }

            var first = knowledge.FirstOrDefault(item => item.MetS >= incident.MetS && IsEvidence(item, evidence));
            if (first is not null) discoveries.Add(new DebriefTimelineEvent(first.MetS, DebriefTimelineKind.Discovery, key));
        }
        return discoveries;
    }

    /// <summary>A measured or downlinked reading of an evidence fact, or suspicion or confirmed damage of an evidence component.</summary>
    private static bool IsEvidence(DebriefKnowledgeItem item, IncidentEvidence evidence) => item.Kind switch
    {
        DebriefKnowledgeKind.Observation => item.Method != ObservationMethod.Model && evidence.Facts.Contains(item.Subject, StringComparer.Ordinal),
        DebriefKnowledgeKind.ComponentSuspected or DebriefKnowledgeKind.ComponentDamaged => evidence.Components.Contains(item.Subject, StringComparer.Ordinal),
        _ => false,
    };

    /// <summary>
    /// The earliest knowledge, at or after an incident of the timeline, that the incident's
    /// <see cref="DebriefDefinition.CauseEvidence"/> names; null when no incident's cause was identified.
    /// </summary>
    private CauseIdentification? IdentifiedCause(DebriefFlightFacts facts)
    {
        var knowledge = facts.Knowledge ?? [];
        CauseIdentification? earliest = null;
        foreach (var incident in facts.Timeline ?? [])
        {
            if (incident.Kind != DebriefTimelineKind.Incident || !incident.Key.StartsWith(DebriefTimelineKeys.IncidentPrefix, StringComparison.Ordinal)) continue;
            if (!_debrief.CauseEvidence.TryGetValue(incident.Key[DebriefTimelineKeys.IncidentPrefix.Length..], out var ways)) continue;
            foreach (var item in knowledge)
            {
                if (item.MetS < incident.MetS || item.MetS >= (earliest?.MetS ?? double.PositiveInfinity)) continue;
                if (ways.FirstOrDefault(way => IdentifiesCause(item, way)) is { } way) earliest = new CauseIdentification(way.Method, item.MetS);
            }
        }
        return earliest;
    }

    /// <summary>A measured or downlinked reading of a cause fact, or confirmed damage of a cause component.</summary>
    private static bool IdentifiesCause(DebriefKnowledgeItem item, CauseEvidence evidence) => item.Kind switch
    {
        DebriefKnowledgeKind.Observation => item.Method != ObservationMethod.Model && evidence.Facts.Contains(item.Subject, StringComparer.Ordinal),
        DebriefKnowledgeKind.ComponentDamaged => evidence.Components.Contains(item.Subject, StringComparer.Ordinal),
        _ => false,
    };

    /// <summary>The event's text; a decision's placeholders name its arguments (<see cref="DecisionArgumentText"/>).</summary>
    private string TimelineText(DebriefTimelineEvent timelineEvent, SortedSet<string> missing)
    {
        var key = timelineEvent.Key;
        if (!_debrief.Explanations.TryGetValue(key, out var template) || string.IsNullOrWhiteSpace(template))
        {
            missing.Add("explanations." + key);
            template = Labels.NeutralText;
        }

        var action = timelineEvent.Kind == DebriefTimelineKind.Decision && key.StartsWith(DebriefTimelineKeys.DecisionPrefix, StringComparison.Ordinal)
            ? TryActionId(key[DebriefTimelineKeys.DecisionPrefix.Length..])
            : null;
        return TextTemplate.Fill(
            template,
            name => action is { } id && timelineEvent.Arguments is { } arguments && arguments.TryGetValue(name, out var value)
                ? DecisionArgumentText.Resolve(_debrief, _mission, id, name, value)
                : null,
            name => missing.Add($"explanations.{key}{{{name}}}"));
    }

    /// <summary>The newest observations and component findings at <paramref name="metS"/>, newest first.</summary>
    private string[] KnownAt(double metS, IReadOnlyList<DebriefKnowledgeItem> knowledge, SortedSet<string> missing)
    {
        var observations = new Dictionary<string, DebriefKnowledgeItem>(StringComparer.Ordinal);
        var findings = new Dictionary<string, DebriefKnowledgeItem>(StringComparer.Ordinal);
        foreach (var item in knowledge)
        {
            if (item.MetS > metS) continue;
            switch (item.Kind)
            {
                case DebriefKnowledgeKind.Observation:
                    // A component condition report is shown through the component's finding.
                    if (_mission?.Fact(item.Subject)?.ConfirmsComponent is null) observations[item.Subject] = item;
                    break;
                case DebriefKnowledgeKind.ComponentCleared:
                    findings.Remove(item.Subject);
                    break;
                default:
                    findings[item.Subject] = item;
                    break;
            }
        }

        return [.. observations.Values.Concat(findings.Values)
            .OrderByDescending(i => i.MetS)
            .ThenBy(i => i.Subject, StringComparer.Ordinal)
            .Take(MaxKnownItems)
            .Select(i => i.Kind == DebriefKnowledgeKind.Observation ? DescribeObservation(i, missing) : DescribeFinding(i, missing))];
    }

    private string DescribeObservation(DebriefKnowledgeItem item, SortedSet<string> missing)
    {
        var knowledge = _debrief.Knowledge;
        var definition = _mission?.Fact(item.Subject);
        var fact = definition?.SourceCrew is { } crew
            ? FillKnowledge("crewFact", knowledge.CrewFact, new() { ["crewName"] = SubjectName(crew.Value, missing) }, missing)
            : definition?.Label ?? MissingName("fact:" + item.Subject, missing);
        var reading = item.Value is { } number
            ? number.ToString("F" + (definition?.Decimals ?? 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
              + (string.IsNullOrEmpty(definition?.Unit) ? "" : " " + definition!.Unit)
            : item.Text ?? TextTemplate.MissingValue;
        var how = item.Method is { } method
            ? Lookup(knowledge.Methods, "knowledge.methods", ContentChecks.SnakeName(method), missing, TextTemplate.MissingValue)
            : TextTemplate.MissingValue;
        var source = item.Observer is { } observer
            ? FillKnowledge("observedBy", knowledge.ObservedBy, new() { ["how"] = how, ["observer"] = SubjectName(observer.Value, missing) }, missing)
            : how;
        return FillKnowledge("observation", knowledge.Observation,
            new() { ["fact"] = fact, ["reading"] = reading, ["source"] = source, ["met"] = ContentFormat.Met(item.MetS) }, missing);
    }

    private string DescribeFinding(DebriefKnowledgeItem item, SortedSet<string> missing)
    {
        var knowledge = _debrief.Knowledge;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["component"] = _mission?.ComponentName(item.Subject) ?? MissingName("component:" + item.Subject, missing),
            ["met"] = ContentFormat.Met(item.MetS),
        };
        if (item.Kind == DebriefKnowledgeKind.ComponentSuspected) return FillKnowledge("suspected", knowledge.Suspected, values, missing);

        if (item.Text is { Length: > 0 } variant)
        {
            if (knowledge.Variants.TryGetValue(variant, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                values["variant"] = text;
                return FillKnowledge("damagedVariant", knowledge.DamagedVariant, values, missing);
            }
            missing.Add("knowledge.variants." + variant);
        }
        return FillKnowledge("damaged", knowledge.Damaged, values, missing);
    }

    private static string FillKnowledge(string name, string template, Dictionary<string, string> values, SortedSet<string> missing) =>
        TextTemplate.Fill(template, values, placeholder => missing.Add($"knowledge.{name}{{{placeholder}}}"));

    /// <summary>A name the mission data lacks is reported and shown as a dash, never as the raw id.</summary>
    private static string MissingName(string key, SortedSet<string> missing)
    {
        missing.Add(key);
        return TextTemplate.MissingValue;
    }

    private static ActionId? TryActionId(string value)
    {
        try
        {
            return new ActionId(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private DebriefHistoryCardView HistoryCard(SortedSet<string> missing)
    {
        var card = _debrief.HistoryCard;
        ImageSlot? slot = null;
        if (card.ImageSlotId is { } slotId && !_images.TryGetValue(slotId, out slot)) missing.Add("imageSlots." + slotId);
        return new DebriefHistoryCardView(
            card.Label,
            card.Title,
            card.Text,
            card.ImageSlotId,
            card.Caption ?? slot?.Caption,
            card.AltText ?? slot?.AltText,
            slot?.Credit,
            card.Credit)
        {
            NasaId = card.NasaId ?? slot?.NasaId,
        };
    }

    private DebriefComparisonView Comparison(DebriefFlightFacts facts, IReadOnlyList<string> lostCrew, SortedSet<string> missing)
    {
        var lines = new List<DebriefComparisonLine>();
        var met = (double? metS) => metS is { } value ? ContentFormat.Met(value) : null;
        var identified = facts.CauseIdentified ?? IdentifiedCause(facts);

        if (facts.DockedMetS is { } docked)
        {
            AddLine(lines, DebriefComparisonKeys.DockingCompleted, n => n == "met" ? met(docked) : null, missing);
            if (facts.UndockedMetS is { } undocked)
            {
                // What the ground knew when it undocked: the 1966 crew undocked without knowing the cause.
                var key = identified is { } known && known.MetS <= undocked ? DebriefComparisonKeys.UndockedCauseKnown : DebriefComparisonKeys.Undocked;
                AddLine(lines, key, n => n == "met" ? met(undocked) : null, missing);
            }
            else
            {
                AddLine(lines, DebriefComparisonKeys.StayedDocked, _ => null, missing);
            }
        }
        else
        {
            AddLine(lines, DebriefComparisonKeys.DockingNotCompleted, _ => null, missing);
        }

        AddLine(lines, DebriefComparisonKeys.PeakRate, n => n == "peakRollDps" ? ContentFormat.WholeNumber(facts.PeakRollDps) : null, missing);

        if (identified is { } cause)
        {
            var method = Lookup(_debrief.Comparison, "comparison", DebriefComparisonKeys.CauseMethod(cause.Method), missing, TextTemplate.MissingValue);
            AddLine(lines, DebriefComparisonKeys.CauseFound, n => n switch
            {
                "method" => method,
                "met" => met(cause.MetS),
                _ => null,
            }, missing);
        }
        else
        {
            AddLine(lines, DebriefComparisonKeys.CauseNotFound, _ => null, missing);
        }

        if (facts.Approach is { } approach)
            AddLine(lines, approach == RecoveryApproach.A ? DebriefComparisonKeys.PathA : DebriefComparisonKeys.PathB, _ => null, missing);

        AddLine(lines, facts.EvaCompleted ? DebriefComparisonKeys.EvaCompleted : DebriefComparisonKeys.EvaNotCompleted, _ => null, missing);

        if (facts.SplashdownMetS is { } splashdown)
        {
            var zone = facts.LandingZoneName ?? (facts.LandingZoneId is { } zoneId ? SubjectName(zoneId, missing) : Labels.OpenOcean);
            AddLine(lines, DebriefComparisonKeys.Landing, n => n switch
            {
                "zoneName" => zone,
                "met" => met(splashdown),
                _ => null,
            }, missing);
            if (facts.RecoveryWaitHours is { } wait)
                AddLine(lines, DebriefComparisonKeys.RecoveryWait, n => n == "waitHours" ? ContentFormat.OneDecimal(wait) : null, missing);
        }

        foreach (var crew in lostCrew)
        {
            var name = SubjectName(crew, missing);
            AddLine(lines, DebriefComparisonKeys.CrewLost, n => n == "crewName" ? name : null, missing);
        }

        return new DebriefComparisonView(
            _debrief.Labels.SimulationResult,
            Lookup(_debrief.Comparison, "comparison", DebriefComparisonKeys.Intro, missing, ""),
            lines,
            Lookup(_debrief.Comparison, "comparison", DebriefComparisonKeys.Outro, missing, ""));
    }

    /// <summary>A comparison line whose template is missing is left out rather than shown half-written.</summary>
    private void AddLine(List<DebriefComparisonLine> lines, string key, Func<string, string?> resolve, SortedSet<string> missing)
    {
        if (!_debrief.Comparison.TryGetValue(key, out var template) || string.IsNullOrWhiteSpace(template))
        {
            missing.Add("comparison." + key);
            return;
        }
        lines.Add(new DebriefComparisonLine(key, TextTemplate.Fill(template, resolve, name => missing.Add($"comparison.{key}{{{name}}}"))));
    }

    /// <param name="subject">Resolved only when the template uses <c>{subject}</c>, so an unused subject is never reported missing.</param>
    private string Explain(
        string key, Func<string?> subject, double? value, double? points, double? maxPoints, string fallback, SortedSet<string> missing)
    {
        if (!_debrief.Explanations.TryGetValue(key, out var template) || string.IsNullOrWhiteSpace(template))
        {
            missing.Add("explanations." + key);
            template = fallback;
        }

        return TextTemplate.Fill(template, name => name switch
        {
            "subject" => subject(),
            "value" => value is { } v ? ContentFormat.WholeNumber(v) : null,
            "points" => points is { } p ? ContentFormat.WholeNumber(p) : null,
            "maxPoints" => maxPoints is { } m ? ContentFormat.WholeNumber(m) : null,
            _ => null,
        }, name => missing.Add($"explanations.{key}{{{name}}}"));
    }

    private Func<string?> SubjectOf(string? subject, SortedSet<string> missing) =>
        () => subject is null ? null : SubjectName(subject, missing);

    /// <summary>Crew id to name, objective id to title, recovery zone id to name, decision key to its text; otherwise the raw id.</summary>
    private string SubjectName(string subject, SortedSet<string> missing)
    {
        if (_crewNames.TryGetValue(subject, out var name)) return name;
        if (_objectiveTitles.TryGetValue(subject, out var title)) return title;
        if (_debrief.ZoneNames.TryGetValue(subject, out var zone) && !string.IsNullOrWhiteSpace(zone)) return zone;
        if (_debrief.Explanations.TryGetValue(subject, out var label) && !string.IsNullOrWhiteSpace(label))
            return TextTemplate.Fill(label, _ => null, placeholder => missing.Add($"explanations.{subject}{{{placeholder}}}"));
        missing.Add("subject:" + subject);
        return subject;
    }

    private static string Lookup(
        IReadOnlyDictionary<string, string> map, string section, string key, SortedSet<string> missing, string? fallback = null)
    {
        if (map.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        missing.Add($"{section}.{key}");
        return fallback ?? key;
    }

    private static List<string> LostCrew(ScoreBreakdown breakdown)
    {
        var lost = new List<string>();
        foreach (var dimension in breakdown.Dimensions)
        {
            if (dimension.Dimension != ScoreDimension.CrewOutcome) continue;
            foreach (var note in dimension.Notes)
            {
                if (note.Key == ScoringExplanationKeys.CrewLostNote && note.Subject is { } crew) lost.Add(crew);
            }
        }
        return lost;
    }
}
