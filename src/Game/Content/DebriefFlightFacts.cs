using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;

namespace Usa.Game.Content;

/// <summary>
/// Flight facts for the debrief that the score breakdown does not carry: the history comparison values
/// and, per lost objective, which feasibility rule took it away. Filled by the integration step at mission end.
/// </summary>
/// <param name="PeakRollDps">Highest absolute roll rate of the flight.</param>
/// <param name="DockedMetS">MET of docking; null when the crew never docked.</param>
/// <param name="UndockedMetS">MET of undocking after docking; null when the vehicles stayed docked.</param>
/// <param name="CauseIdentified">
/// How and when an incident's cause was identified in flight. Null lets the composer derive it: the first knowledge after an
/// incident that the debrief's <see cref="DebriefDefinition.CauseEvidence"/> names; never identified when there is none.
/// </param>
/// <param name="Approach">The recovery path taken; null when neither was.</param>
/// <param name="SplashdownMetS">Null when the capsule never landed.</param>
/// <param name="LandingZoneName">
/// Display name of the zone the capsule landed in (the zone the recovery-zone score line names); null outside every
/// zone, which reads as <see cref="DebriefLabels.OpenOcean"/>. The score line follows the scorer: outside every zone it names
/// the open ocean whatever this holds.
/// </param>
/// <param name="ObjectiveReasonKeys">
/// Per objective, the <see cref="DebriefDefinition.FeasibilityReasons"/> key that explains its loss
/// (<c>o3_eva.oams_propellant_low</c> or <c>rule.rcs_activation_return</c>).
/// </param>
/// <param name="Timeline">Incidents and player decisions; the composer adds each incident's discovery from <paramref name="Knowledge"/>.</param>
/// <param name="Knowledge">Everything the ground learned during the flight, oldest first (GDD 4).</param>
/// <param name="LandingZoneId">
/// The recovery zone the capsule landed in; the composer names it from <see cref="DebriefDefinition.ZoneNames"/> unless
/// <paramref name="LandingZoneName"/> already does. Null outside every zone.
/// </param>
/// <param name="CrewLosses">Each astronaut who died in flight and when, in MET order; null when nobody died.</param>
public sealed record DebriefFlightFacts(
    double PeakRollDps,
    double? DockedMetS = null,
    double? UndockedMetS = null,
    CauseIdentification? CauseIdentified = null,
    RecoveryApproach? Approach = null,
    bool EvaCompleted = false,
    double? SplashdownMetS = null,
    string? LandingZoneName = null,
    double? RecoveryWaitHours = null,
    IReadOnlyDictionary<ObjectiveId, string>? ObjectiveReasonKeys = null,
    IReadOnlyList<DebriefCrewCondition>? CrewConditions = null,
    IReadOnlyList<DebriefTimelineEvent>? Timeline = null,
    IReadOnlyList<DebriefKnowledgeItem>? Knowledge = null,
    string? LandingZoneId = null,
    IReadOnlyList<DebriefCrewLoss>? CrewLosses = null);

/// <summary>An astronaut lost in flight (GDD D10: the mission goes on with the survivors).</summary>
public sealed record DebriefCrewLoss(CrewId Crew, double MetS);

/// <param name="Method">
/// How the cause was identified: a <see cref="CauseEvidence.Method"/> of the mission's debrief, worded by the comparison text
/// <c>causeMethod.&lt;method&gt;</c>.
/// </param>
public sealed record CauseIdentification(string Method, double MetS);

public enum RecoveryApproach
{
    /// <summary>Isolated the faulty thruster and kept the main system working.</summary>
    A,

    /// <summary>Shut the main thrusters down and stopped the spin with the re-entry control thrusters.</summary>
    B,
}

/// <summary>One astronaut's health, 0-100 points, at launch, at its lowest and at recovery (GDD 10.4).</summary>
/// <param name="RecoveryHealth">Null when the astronaut did not come home.</param>
public sealed record DebriefCrewCondition(CrewId Crew, double LaunchHealth, double WorstHealth, double? RecoveryHealth);

public enum DebriefTimelineKind
{
    /// <summary>The failure itself, e.g. the thruster sticking open.</summary>
    Incident,

    /// <summary>The ground or crew first saw it.</summary>
    Discovery,

    /// <summary>A major player decision.</summary>
    Decision,
}

/// <summary>A mark on the debrief timeline (GDD 10.4: incident, discovery, what was known at each major decision).</summary>
/// <param name="Key">The event's text in <see cref="DebriefDefinition.Explanations"/> (<see cref="DebriefTimelineKeys"/>).</param>
/// <param name="Arguments">
/// A decision's action arguments by parameter name as canonical text (a component id, an enum value, <c>true</c>, a number);
/// the decision text names them through placeholders of the same name. Null when there are none or they were not recorded.
/// </param>
public sealed record DebriefTimelineEvent(double MetS, DebriefTimelineKind Kind, string Key, IReadOnlyDictionary<string, string>? Arguments = null);

public enum DebriefKnowledgeKind
{
    /// <summary>A fact was observed; <see cref="DebriefKnowledgeItem.Subject"/> is the fact id.</summary>
    Observation,

    /// <summary>A clue pointed at a component; <see cref="DebriefKnowledgeItem.Subject"/> is the component id.</summary>
    ComponentSuspected,

    /// <summary>An inspection or test confirmed a component damaged; <see cref="DebriefKnowledgeItem.Text"/> is the damage variant.</summary>
    ComponentDamaged,

    /// <summary>A suspected or damaged component was cleared (confirmed nominal, repaired, or its clue withdrawn).</summary>
    ComponentCleared,
}

/// <summary>One thing the ground learned, with raw ids and values; the composer words it with display names.</summary>
/// <param name="Value">An observation's number.</param>
/// <param name="Text">An observation's text value, or a damaged component's variant (e.g. <c>stuck_open</c>).</param>
/// <param name="Method">How an observation was made.</param>
/// <param name="Observer">The crew member who made an observation; null for downlink and model values.</param>
public sealed record DebriefKnowledgeItem(
    double MetS,
    DebriefKnowledgeKind Kind,
    string Subject,
    double? Value = null,
    string? Text = null,
    ObservationMethod? Method = null,
    CrewId? Observer = null);
