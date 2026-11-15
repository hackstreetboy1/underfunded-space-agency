using Usa.Simulation.Data;

namespace Usa.Game.Content;

/// <summary>Root of <c>content/&lt;missionId&gt;/debrief.json</c> (schema <c>debrief/1</c>).</summary>
/// <param name="Placeholders">Every <c>{name}</c> placeholder the texts may use, with what fills it (for writers).</param>
/// <param name="Dimensions">Display name per snake_case <c>ScoreDimension</c>.</param>
/// <param name="ObjectiveStates">Text per snake_case <c>ObjectiveFinalState</c>.</param>
/// <param name="LossCauses">Text per snake_case <c>ObjectiveLossCause</c>.</param>
/// <param name="FeasibilityReasons">
/// Why an objective was lost: <c>&lt;objectiveId&gt;.&lt;feasibilityRuleId&gt;</c> or <c>rule.&lt;missionRuleId&gt;</c>.
/// </param>
/// <param name="TimelineKinds">Display name per snake_case <see cref="DebriefTimelineKind"/>.</param>
/// <param name="Comparison">Templates comparing the flight with history; keys in <see cref="DebriefComparisonKeys"/>.</param>
/// <param name="Explanations">
/// Text per Scoring explanation key (see <see cref="ScoringExplanationKeys"/>), per scenario decision key and per timeline
/// key (see <see cref="DebriefTimelineKeys"/>).
/// </param>
/// <param name="ZoneNames">Display name per recovery zone id of the mission's <c>flight.json</c> (which has none).</param>
/// <param name="IncidentEvidence">
/// Per incident id, the knowledge causally related to it: the incident is discovered by the first observation of one of its
/// facts, or suspicion or confirmed damage of one of its components, after it happened. Kept here because the mission's
/// <c>incidents.json</c> belongs to the mission data owner.
/// </param>
/// <param name="Knowledge">Wording of the "what you knew then" lines of the timeline.</param>
/// <param name="DecisionValues">
/// Display text of a decision argument the vehicle data cannot name, keyed <c>&lt;actionId&gt;.&lt;parameter&gt;.&lt;value&gt;</c>
/// (e.g. <c>cycle_oams_breakers.group.t05_t08</c>, <c>oams_master.on.true</c>).
/// </param>
/// <param name="CauseEvidence">
/// Per incident id, the knowledge whose arrival after the incident identifies its cause, each entry with the method the
/// comparison names (<c>comparison['causeMethod.&lt;method&gt;']</c>). Incidents without an entry are never reported as
/// identified. Kept here, like <paramref name="IncidentEvidence"/>, because the mission files belong to the mission data owner.
/// </param>
public sealed record DebriefDefinition(
    string Schema,
    string MissionId,
    IReadOnlyDictionary<string, string> Placeholders,
    DebriefLabels Labels,
    IReadOnlyDictionary<string, string> Dimensions,
    IReadOnlyDictionary<string, string> ObjectiveStates,
    IReadOnlyDictionary<string, string> LossCauses,
    IReadOnlyDictionary<string, string> FeasibilityReasons,
    IReadOnlyDictionary<string, string> TimelineKinds,
    HistoryCard HistoryCard,
    string FictionalAdaptationText,
    IReadOnlyDictionary<string, string> Comparison,
    IReadOnlyDictionary<string, string> Explanations,
    IReadOnlyDictionary<string, string> ZoneNames,
    IReadOnlyDictionary<string, IncidentEvidence> IncidentEvidence,
    DebriefKnowledgeTexts Knowledge,
    IReadOnlyDictionary<string, string> DecisionValues,
    IReadOnlyDictionary<string, IReadOnlyList<CauseEvidence>> CauseEvidence) : IVersionedDefinition
{
    public const string SchemaTag = "debrief/1";

    /// <summary>Prefix of feasibility reasons that come from a mission rule rather than one objective.</summary>
    public const string MissionRuleReasonPrefix = "rule";

    public static string PathFor(string missionId) => $"content/{missionId}/debrief.json";
}

/// <param name="DocumentedHistory">Provenance label (GDD 10.4).</param>
/// <param name="SimulationResult">Provenance label (GDD 10.4).</param>
/// <param name="FictionalAdaptation">Provenance label (GDD 10.4).</param>
/// <param name="CapNote">Template with <c>{cap}</c>, <c>{lost}</c>, <c>{crewCount}</c>.</param>
/// <param name="CrewCondition">
/// Heading of the crew condition table; <paramref name="CrewLaunch"/>, <paramref name="CrewWorst"/> and
/// <paramref name="CrewRecovery"/> head its columns.
/// </param>
/// <param name="KnownAtTheTime">Heading of what was known at a discovery or decision; <paramref name="NothingKnown"/> when that list is empty.</param>
/// <param name="OpenOcean">The landing place when the capsule came down outside every recovery zone.</param>
/// <param name="NeutralScoreLine">Stands in for a score line without explanation; may use <c>{points}</c> and <c>{maxPoints}</c>.</param>
/// <param name="NeutralText">Stands in for any other missing explanation or reason.</param>
public sealed record DebriefLabels(
    string DocumentedHistory,
    string SimulationResult,
    string FictionalAdaptation,
    string CapNote,
    string BestDecisions,
    string LargestAvoidableLoss,
    string SplusTitle,
    string Replay,
    string Retry,
    string MissionSelect,
    string CrewCondition,
    string CrewLaunch,
    string CrewWorst,
    string CrewRecovery,
    string Timeline,
    string KnownAtTheTime,
    string NothingKnown,
    string OpenOcean,
    string NeutralScoreLine,
    string NeutralText);

/// <summary>Fact ids (patterns resolved, e.g. <c>oams.temp.t08</c>) and component ids whose news counts as discovering an incident.</summary>
public sealed record IncidentEvidence(IReadOnlyList<string> Facts, IReadOnlyList<string> Components);

/// <summary>
/// One way an incident's cause is identified: a measured or reported reading of one of <paramref name="Facts"/> (patterns
/// resolved, e.g. <c>oams.temp.t08</c>), or confirmed damage of one of <paramref name="Components"/>, after the incident.
/// Suspicion alone does not identify a cause.
/// </summary>
/// <param name="Method">Snake_case name of the method; the comparison words it as <c>causeMethod.&lt;method&gt;</c>.</param>
public sealed record CauseEvidence(string Method, IReadOnlyList<string> Facts, IReadOnlyList<string> Components);

/// <summary>Templates for one "what you knew then" line; each takes only the placeholders listed on it.</summary>
/// <param name="Observation"><c>{fact}</c>, <c>{reading}</c>, <c>{source}</c>, <c>{met}</c>.</param>
/// <param name="ObservedBy">The <c>{source}</c> of an observation a crew member made: <c>{how}</c>, <c>{observer}</c>.</param>
/// <param name="Suspected"><c>{component}</c>, <c>{met}</c>.</param>
/// <param name="Damaged"><c>{component}</c>, <c>{met}</c>.</param>
/// <param name="DamagedVariant"><c>{component}</c>, <c>{variant}</c>, <c>{met}</c>.</param>
/// <param name="CrewFact">The <c>{fact}</c> of a crew member's self-report (the catalog label names the crew id): <c>{crewName}</c>.</param>
/// <param name="Methods">How an observation was made, per snake_case <c>ObservationMethod</c>; also the <c>{source}</c> without an observer.</param>
/// <param name="Variants">Damage variant words (the part after ':' of a condition report, e.g. <c>stuck_open</c>).</param>
public sealed record DebriefKnowledgeTexts(
    string Observation,
    string ObservedBy,
    string Suspected,
    string Damaged,
    string DamagedVariant,
    string CrewFact,
    IReadOnlyDictionary<string, string> Methods,
    IReadOnlyDictionary<string, string> Variants)
{
    public static IReadOnlyList<string> ObservationPlaceholders { get; } = ["fact", "reading", "source", "met"];

    public static IReadOnlyList<string> ObservedByPlaceholders { get; } = ["how", "observer"];

    public static IReadOnlyList<string> FindingPlaceholders { get; } = ["component", "met"];

    public static IReadOnlyList<string> VariantFindingPlaceholders { get; } = ["component", "variant", "met"];

    public static IReadOnlyList<string> CrewFactPlaceholders { get; } = ["crewName"];
}

/// <summary>
/// The real-history card: what actually happened, without programme names. Its image is either a briefing image slot
/// (<paramref name="ImageSlotId"/>) or a curated item of its own (<paramref name="NasaId"/>, the manifest item id, for a
/// photo the briefing does not show); exactly one of the two is set.
/// </summary>
/// <param name="ImageSlotId">An image slot of the mission briefing; its caption, alt text and credit come with it.</param>
/// <param name="NasaId">
/// <c>itemId</c> of a <c>content/nasa/manifest.json</c> item (touchpoint <c>debrief</c>). The card then needs its own
/// <paramref name="Caption"/>, <paramref name="AltText"/> and a <paramref name="Credit"/> line that names the item.
/// </param>
/// <param name="Caption">Shown under the image; overrides the slot's caption when both are set.</param>
/// <param name="AltText">Overrides the slot's alt text when both are set.</param>
public sealed record HistoryCard(
    string Label,
    string Title,
    string Text,
    string? ImageSlotId = null,
    string? NasaId = null,
    string? Caption = null,
    string? AltText = null,
    string? Credit = null);
