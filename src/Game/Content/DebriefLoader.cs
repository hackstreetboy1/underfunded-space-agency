using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Scoring;

namespace Usa.Game.Content;

/// <summary>
/// Loads and validates a mission debrief: every Scoring explanation key, scenario decision key, comparison template and
/// timeline key the mission can produce needs a text, and every id the debrief names must exist in the mission data.
/// </summary>
public static class DebriefLoader
{
    /// <exception cref="DefinitionException">Every problem in the file (and in the mission files it refers to) at once.</exception>
    public static DebriefDefinition Load(IDataSource source, string missionId)
    {
        var file = DebriefDefinition.PathFor(missionId);
        var debrief = new DefinitionLoader(source).Load<DebriefDefinition>(file, DebriefDefinition.SchemaTag);
        var errors = new DefinitionErrors();
        var mission = DebriefMissionReferences.Load(source, missionId, errors);
        Validate(debrief, file, missionId, errors, mission);
        errors.ThrowIfAny();
        return debrief;
    }

    /// <param name="mission">
    /// The mission data the debrief refers to (<see cref="DebriefMissionReferences.Load"/>): every timeline key, scenario decision
    /// key, incident, recovery zone and incident damage variant needs a text, and no other may have one. Null skips those checks.
    /// </param>
    public static void Validate(
        DebriefDefinition debrief, string file, string missionId, DefinitionErrors errors, DebriefMissionReferences? mission = null)
    {
        ArgumentNullException.ThrowIfNull(debrief);
        ArgumentNullException.ThrowIfNull(errors);

        if (debrief.MissionId != missionId) errors.Add(file, "$.missionId", $"Expected mission '{missionId}' but found '{debrief.MissionId}'.");

        ContentChecks.TextMap(errors, file, "$.placeholders", debrief.Placeholders);
        foreach (var key in debrief.Placeholders.Keys)
        {
            if (key.Length < 3 || key[0] != '{' || key[^1] != '}' || TextTemplate.Placeholders(key).Count != 1)
                errors.Add(file, ContentChecks.KeyPath("$.placeholders", key), "Placeholder keys look like \"{name}\".");
        }

        foreach (var (name, text) in LabelTexts(debrief.Labels)) ContentChecks.Text(errors, file, "$.labels." + name, text);

        CheckEnumMap<ScoreDimension>(errors, file, "$.dimensions", debrief.Dimensions, "dimension");
        CheckEnumMap<ObjectiveFinalState>(errors, file, "$.objectiveStates", debrief.ObjectiveStates, "objective state");
        CheckEnumMap<ObjectiveLossCause>(errors, file, "$.lossCauses", debrief.LossCauses, "loss cause");
        CheckEnumMap<DebriefTimelineKind>(errors, file, "$.timelineKinds", debrief.TimelineKinds, "timeline kind");

        ContentChecks.TextMap(errors, file, "$.feasibilityReasons", debrief.FeasibilityReasons);
        foreach (var key in debrief.FeasibilityReasons.Keys)
        {
            var dot = key.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0 || dot == key.Length - 1)
                errors.Add(file, ContentChecks.KeyPath("$.feasibilityReasons", key), "Keys look like \"<objectiveId>.<ruleId>\" or \"rule.<ruleId>\".");
        }

        var card = debrief.HistoryCard;
        ContentChecks.Text(errors, file, "$.historyCard.label", card.Label);
        ContentChecks.Text(errors, file, "$.historyCard.title", card.Title);
        ContentChecks.Text(errors, file, "$.historyCard.text", card.Text);
        CheckHistoryCardImage(card, file, errors);
        ContentChecks.Text(errors, file, "$.fictionalAdaptationText", debrief.FictionalAdaptationText);

        ContentChecks.TextMap(errors, file, "$.comparison", debrief.Comparison);
        ContentChecks.RequiredKeys(errors, file, "$.comparison", debrief.Comparison, DebriefComparisonKeys.All, "comparison text");

        ContentChecks.TextMap(errors, file, "$.explanations", debrief.Explanations);
        ContentChecks.RequiredKeys(errors, file, "$.explanations", debrief.Explanations, ScoringExplanationKeys.All, "explanation for Scoring key");
        if (mission is not null)
            ContentChecks.RequiredKeys(errors, file, "$.explanations", debrief.Explanations, mission.DecisionKeys, "text for scenario decision key");
        CheckTimeline(debrief, file, mission, errors);

        CheckZoneNames(debrief, file, mission, errors);
        CheckIncidentEvidence(debrief, file, mission, errors);
        CheckCauseEvidence(debrief, file, mission, errors);
        CheckKnowledge(debrief.Knowledge, file, mission, errors);
        CheckDecisionValues(debrief, file, mission, errors);

        CheckPlaceholdersDeclared(debrief, file, errors);
    }

    private static void CheckTimeline(DebriefDefinition debrief, string file, DebriefMissionReferences? mission, DefinitionErrors errors)
    {
        if (mission is not null)
            ContentChecks.RequiredKeys(errors, file, "$.explanations", debrief.Explanations, mission.TimelineKeys, "timeline text");

        foreach (var (key, text) in debrief.Explanations)
        {
            if (!key.StartsWith(DebriefTimelineKeys.Prefix, StringComparison.Ordinal)) continue;
            var path = ContentChecks.KeyPath("$.explanations", key);
            if (mission is not null && !mission.TimelineKeys.Contains(key))
            {
                errors.Add(file, path, $"No incident or crew-task/remote action of mission '{debrief.MissionId}' produces timeline key '{key}'.");
                continue;
            }

            var names = TextTemplate.Placeholders(text);
            if (names.Count == 0) continue;
            if (!key.StartsWith(DebriefTimelineKeys.DecisionPrefix, StringComparison.Ordinal))
            {
                // The composer fills nothing into an incident or discovery text, so a placeholder would reach the player as a dash.
                foreach (var name in names) errors.Add(file, path, $"Incident and discovery texts take no placeholders; '{{{name}}}' would show as a dash.");
                continue;
            }

            if (mission is not null) CheckDecisionPlaceholders(debrief, mission, key, names, path, file, errors);
        }
    }

    /// <summary>A decision text may name a parameter of its crew-task or remote action that the debrief can word for every value.</summary>
    private static void CheckDecisionPlaceholders(
        DebriefDefinition debrief, DebriefMissionReferences mission, string key, IReadOnlyList<string> names, string path, string file, DefinitionErrors errors)
    {
        if (!mission.TryGetDecisionAction(new ActionId(key[DebriefTimelineKeys.DecisionPrefix.Length..]), out var action)) return;
        foreach (var name in names)
        {
            var parameter = action.Parameters?.FirstOrDefault(p => p.Name == name);
            if (parameter is null)
            {
                errors.Add(file, path, $"Action '{action.Id}' has no parameter '{name}'.");
                continue;
            }

            var unnamed = DecisionArgumentText.Unnamed(debrief, mission, action, parameter);
            if (unnamed is null)
                errors.Add(file, path, $"Parameter '{name}' of '{action.Id}' is free text and cannot be named in player text.");
            else if (unnamed.Count > 0)
                errors.Add(file, path, $"No display text for '{name}' value(s) {string.Join(", ", unnamed)}: add $.decisionValues['{DecisionArgumentText.ValueKey(action.Id, name, unnamed[0])}'] (or a component the vehicle data names).");
        }
    }

    private static void CheckZoneNames(DebriefDefinition debrief, string file, DebriefMissionReferences? mission, DefinitionErrors errors)
    {
        ContentChecks.TextMap(errors, file, "$.zoneNames", debrief.ZoneNames);
        if (mission is null) return;
        ContentChecks.RequiredKeys(errors, file, "$.zoneNames", debrief.ZoneNames, mission.RecoveryZones, "recovery zone name");
        ContentChecks.KnownKeys(errors, file, "$.zoneNames", debrief.ZoneNames, mission.RecoveryZones, "recovery zone");
    }

    private static void CheckIncidentEvidence(DebriefDefinition debrief, string file, DebriefMissionReferences? mission, DefinitionErrors errors)
    {
        const string Section = "$.incidentEvidence";
        if (debrief.IncidentEvidence.Count == 0) errors.Add(file, Section, "At least one entry is required.");
        var incidents = mission?.Incidents.Select(i => i.Value).ToHashSet(StringComparer.Ordinal);
        if (incidents is not null)
        {
            foreach (var incident in incidents.Order(StringComparer.Ordinal))
            {
                if (!debrief.IncidentEvidence.ContainsKey(incident))
                    errors.Add(file, ContentChecks.KeyPath(Section, incident), $"Missing discovery evidence for incident '{incident}'.");
            }
        }

        foreach (var (incident, evidence) in debrief.IncidentEvidence)
        {
            var path = ContentChecks.KeyPath(Section, incident);
            if (incidents is not null && !incidents.Contains(incident))
                errors.Add(file, path, $"Mission '{debrief.MissionId}' has no incident '{incident}'.");
            if (evidence.Facts.Count + evidence.Components.Count == 0)
                errors.Add(file, path, "List at least one fact or component whose news discovers the incident.");

            for (var i = 0; i < evidence.Facts.Count; i++)
            {
                var fact = evidence.Facts[i];
                if (string.IsNullOrWhiteSpace(fact)) errors.Add(file, $"{path}.facts[{i}]", "A fact id is empty.");
                else if (mission is not null && mission.Fact(fact) is null) errors.Add(file, $"{path}.facts[{i}]", $"Unknown fact '{fact}'.");
            }

            for (var i = 0; i < evidence.Components.Count; i++)
            {
                var component = evidence.Components[i];
                if (string.IsNullOrWhiteSpace(component)) errors.Add(file, $"{path}.components[{i}]", "A component id is empty.");
                else if (mission is not null && !mission.IsComponent(component)) errors.Add(file, $"{path}.components[{i}]", $"No vehicle of the mission has component '{component}'.");
            }
        }
    }

    /// <summary>
    /// Each incident's cause evidence names real facts and components, and every method has exactly one comparison wording:
    /// a <c>causeMethod.*</c> text without a method would never show, a method without one would show as a dash.
    /// </summary>
    private static void CheckCauseEvidence(DebriefDefinition debrief, string file, DebriefMissionReferences? mission, DefinitionErrors errors)
    {
        const string Section = "$.causeEvidence";
        var incidents = mission?.Incidents.Select(i => i.Value).ToHashSet(StringComparer.Ordinal);
        var methods = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (incident, ways) in debrief.CauseEvidence)
        {
            var path = ContentChecks.KeyPath(Section, incident);
            if (incidents is not null && !incidents.Contains(incident))
                errors.Add(file, path, $"Mission '{debrief.MissionId}' has no incident '{incident}'.");
            if (ways.Count == 0) errors.Add(file, path, "List at least one way the cause is identified, or leave the incident out.");

            for (var i = 0; i < ways.Count; i++)
            {
                var way = ways[i];
                var wayPath = $"{path}[{i}]";
                if (!IsSnakeName(way.Method)) errors.Add(file, wayPath + ".method", $"Method '{way.Method}' is not a snake_case name.");
                else methods.Add(way.Method);
                if (way.Facts.Count + way.Components.Count == 0)
                    errors.Add(file, wayPath, "List at least one fact or component whose news identifies the cause.");

                for (var j = 0; j < way.Facts.Count; j++)
                {
                    var fact = way.Facts[j];
                    if (string.IsNullOrWhiteSpace(fact)) errors.Add(file, $"{wayPath}.facts[{j}]", "A fact id is empty.");
                    else if (mission is not null && mission.Fact(fact) is null) errors.Add(file, $"{wayPath}.facts[{j}]", $"Unknown fact '{fact}'.");
                }

                for (var j = 0; j < way.Components.Count; j++)
                {
                    var component = way.Components[j];
                    if (string.IsNullOrWhiteSpace(component)) errors.Add(file, $"{wayPath}.components[{j}]", "A component id is empty.");
                    else if (mission is not null && !mission.IsComponent(component))
                        errors.Add(file, $"{wayPath}.components[{j}]", $"No vehicle of the mission has component '{component}'.");
                }
            }
        }

        var wordings = methods.Select(DebriefComparisonKeys.CauseMethod).ToArray();
        ContentChecks.RequiredKeys(errors, file, "$.comparison", debrief.Comparison, wordings, "wording for cause method");
        foreach (var key in debrief.Comparison.Keys.Order(StringComparer.Ordinal))
        {
            if (key.StartsWith(DebriefComparisonKeys.CauseMethodPrefix, StringComparison.Ordinal) && !wordings.Contains(key, StringComparer.Ordinal))
                errors.Add(file, ContentChecks.KeyPath("$.comparison", key), $"No entry of $.causeEvidence has method '{key[DebriefComparisonKeys.CauseMethodPrefix.Length..]}'.");
        }
    }

    private static bool IsSnakeName(string? name) =>
        !string.IsNullOrEmpty(name) && name[0] is >= 'a' and <= 'z' && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    private static void CheckKnowledge(DebriefKnowledgeTexts knowledge, string file, DebriefMissionReferences? mission, DefinitionErrors errors)
    {
        foreach (var (name, text, allowed) in KnowledgeTemplates(knowledge))
        {
            var path = "$.knowledge." + name;
            ContentChecks.Text(errors, file, path, text);
            foreach (var placeholder in TextTemplate.Placeholders(text ?? ""))
            {
                if (!allowed.Contains(placeholder, StringComparer.Ordinal))
                    errors.Add(file, path, $"'{{{placeholder}}}' would show as a dash: this template takes only {string.Join(", ", allowed.Select(a => "{" + a + "}"))}.");
            }
        }

        CheckEnumMap<ObservationMethod>(errors, file, "$.knowledge.methods", knowledge.Methods, "observation method");
        ContentChecks.TextMap(errors, file, "$.knowledge.variants", knowledge.Variants);
        if (mission is not null)
            ContentChecks.RequiredKeys(errors, file, "$.knowledge.variants", knowledge.Variants, mission.IncidentFaults, "text for damage variant");
    }

    private static void CheckDecisionValues(DebriefDefinition debrief, string file, DebriefMissionReferences? mission, DefinitionErrors errors)
    {
        const string Section = "$.decisionValues";
        ContentChecks.TextMap(errors, file, Section, debrief.DecisionValues);
        foreach (var key in debrief.DecisionValues.Keys)
        {
            var path = ContentChecks.KeyPath(Section, key);
            var first = key.IndexOf('.', StringComparison.Ordinal);
            var second = first < 0 ? -1 : key.IndexOf('.', first + 1);
            if (first <= 0 || second <= first + 1 || second == key.Length - 1)
            {
                errors.Add(file, path, "Keys look like \"<actionId>.<parameter>.<value>\".");
                continue;
            }
            if (mission is null) continue;

            var actionId = key[..first];
            var parameterName = key[(first + 1)..second];
            var value = key[(second + 1)..];
            var parameter = mission.TimelineKeys.Contains(DebriefTimelineKeys.DecisionPrefix + actionId) &&
                            mission.TryGetDecisionAction(new ActionId(actionId), out var action)
                ? action.Parameters?.FirstOrDefault(p => p.Name == parameterName)
                : null;
            IReadOnlyList<string> allowed = parameter is null ? [] : parameter.Type switch
            {
                ParameterType.Enum => parameter.Values ?? [],
                ParameterType.Boolean => ["true", "false"],
                _ => [],
            };
            if (!allowed.Contains(value, StringComparer.Ordinal))
                errors.Add(file, path, $"No crew-task or remote decision of mission '{debrief.MissionId}' has an argument '{parameterName}' = '{value}'.");
        }
    }

    private static void CheckEnumMap<T>(DefinitionErrors errors, string file, string path, IReadOnlyDictionary<string, string> map, string what)
        where T : struct, Enum
    {
        var names = ContentChecks.SnakeNames<T>();
        ContentChecks.TextMap(errors, file, path, map);
        ContentChecks.RequiredKeys(errors, file, path, map, names, what);
        ContentChecks.KnownKeys(errors, file, path, map, names, what);
    }

    /// <summary>A placeholder the composer never fills would reach the player as a dash; catch it at load instead.</summary>
    private static void CheckPlaceholdersDeclared(DebriefDefinition debrief, string file, DefinitionErrors errors)
    {
        foreach (var (path, text) in Texts(debrief))
        {
            foreach (var name in TextTemplate.Placeholders(text))
            {
                if (!debrief.Placeholders.ContainsKey("{" + name + "}"))
                    errors.Add(file, path, $"Placeholder '{{{name}}}' is not declared in $.placeholders.");
            }
        }

        // Names and values are filled into templates, never filled themselves.
        foreach (var (path, text) in Values(debrief))
        {
            foreach (var name in TextTemplate.Placeholders(text))
                errors.Add(file, path, $"Display names take no placeholders; '{{{name}}}' would show as written.");
        }
    }

    private static void CheckHistoryCardImage(HistoryCard card, string file, DefinitionErrors errors)
    {
        ContentChecks.OptionalText(errors, file, "$.historyCard.imageSlotId", card.ImageSlotId);
        ContentChecks.OptionalText(errors, file, "$.historyCard.nasaId", card.NasaId);
        ContentChecks.OptionalText(errors, file, "$.historyCard.credit", card.Credit);
        if ((card.ImageSlotId is null) == (card.NasaId is null))
            errors.Add(file, "$.historyCard", "Set exactly one of imageSlotId (a briefing image) and nasaId (a curated item of the card's own).");

        if (card.NasaId is null)
        {
            ContentChecks.OptionalText(errors, file, "$.historyCard.caption", card.Caption);
            ContentChecks.OptionalText(errors, file, "$.historyCard.altText", card.AltText);
            return;
        }

        // No briefing slot supplies these for a picked item, and every image is shown with its caption (GDD 10.4).
        ContentChecks.Text(errors, file, "$.historyCard.caption", card.Caption);
        ContentChecks.Text(errors, file, "$.historyCard.altText", card.AltText);
        if (card.Credit is null)
            errors.Add(file, "$.historyCard.credit", "A picked item (nasaId set) needs its credit line.");
        else if (!card.Credit.Contains(card.NasaId, StringComparison.OrdinalIgnoreCase))
            errors.Add(file, "$.historyCard.credit", $"The credit line must name the picked item '{card.NasaId}'.");
    }

    private static IEnumerable<(string Path, string Text)> Texts(DebriefDefinition debrief)
    {
        foreach (var (name, text) in LabelTexts(debrief.Labels)) yield return ("$.labels." + name, text);
        foreach (var (key, text) in debrief.FeasibilityReasons) yield return (ContentChecks.KeyPath("$.feasibilityReasons", key), text);
        foreach (var (key, text) in debrief.Comparison) yield return (ContentChecks.KeyPath("$.comparison", key), text);
        foreach (var (key, text) in debrief.Explanations) yield return (ContentChecks.KeyPath("$.explanations", key), text);
        foreach (var (name, text, _) in KnowledgeTemplates(debrief.Knowledge)) yield return ("$.knowledge." + name, text ?? "");
        yield return ("$.historyCard.text", debrief.HistoryCard.Text);
        yield return ("$.fictionalAdaptationText", debrief.FictionalAdaptationText);
    }

    private static IEnumerable<(string Path, string Text)> Values(DebriefDefinition debrief)
    {
        foreach (var (key, text) in debrief.ZoneNames) yield return (ContentChecks.KeyPath("$.zoneNames", key), text);
        foreach (var (key, text) in debrief.DecisionValues) yield return (ContentChecks.KeyPath("$.decisionValues", key), text);
        foreach (var (key, text) in debrief.Knowledge.Methods) yield return (ContentChecks.KeyPath("$.knowledge.methods", key), text);
        foreach (var (key, text) in debrief.Knowledge.Variants) yield return (ContentChecks.KeyPath("$.knowledge.variants", key), text);
        if (debrief.HistoryCard.Caption is { } caption) yield return ("$.historyCard.caption", caption);
        if (debrief.HistoryCard.AltText is { } altText) yield return ("$.historyCard.altText", altText);
    }

    private static (string Name, string? Text, IReadOnlyList<string> Allowed)[] KnowledgeTemplates(DebriefKnowledgeTexts knowledge) =>
    [
        ("observation", knowledge.Observation, DebriefKnowledgeTexts.ObservationPlaceholders),
        ("observedBy", knowledge.ObservedBy, DebriefKnowledgeTexts.ObservedByPlaceholders),
        ("suspected", knowledge.Suspected, DebriefKnowledgeTexts.FindingPlaceholders),
        ("damaged", knowledge.Damaged, DebriefKnowledgeTexts.FindingPlaceholders),
        ("damagedVariant", knowledge.DamagedVariant, DebriefKnowledgeTexts.VariantFindingPlaceholders),
        ("crewFact", knowledge.CrewFact, DebriefKnowledgeTexts.CrewFactPlaceholders),
    ];

    private static (string Name, string Text)[] LabelTexts(DebriefLabels labels) =>
    [
        ("documentedHistory", labels.DocumentedHistory),
        ("simulationResult", labels.SimulationResult),
        ("fictionalAdaptation", labels.FictionalAdaptation),
        ("capNote", labels.CapNote),
        ("bestDecisions", labels.BestDecisions),
        ("largestAvoidableLoss", labels.LargestAvoidableLoss),
        ("splusTitle", labels.SplusTitle),
        ("replay", labels.Replay),
        ("retry", labels.Retry),
        ("missionSelect", labels.MissionSelect),
        ("crewCondition", labels.CrewCondition),
        ("crewLaunch", labels.CrewLaunch),
        ("crewWorst", labels.CrewWorst),
        ("crewRecovery", labels.CrewRecovery),
        ("timeline", labels.Timeline),
        ("knownAtTheTime", labels.KnownAtTheTime),
        ("nothingKnown", labels.NothingKnown),
        ("openOcean", labels.OpenOcean),
        ("neutralScoreLine", labels.NeutralScoreLine),
        ("neutralText", labels.NeutralText),
    ];
}
