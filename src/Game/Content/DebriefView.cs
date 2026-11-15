using Usa.Simulation.Kernel;
using Usa.Simulation.Scoring;

namespace Usa.Game.Content;

/// <summary>Everything the debrief screen shows (UI.md 5), with every text resolved.</summary>
/// <param name="GradeLabel">Band name from the scoring config, e.g. "S+".</param>
/// <param name="CapNote">"Score capped at 50: 1 of 2 crew lost." when any crew member was lost, else null.</param>
/// <param name="MissingKeys">
/// Content gaps hit while composing, sorted: <c>explanations.&lt;key&gt;</c>, <c>comparison.&lt;key&gt;</c>,
/// <c>feasibilityReasons.&lt;key&gt;</c>, <c>knowledge.variants.&lt;variant&gt;</c>, <c>incidentEvidence.&lt;incident&gt;</c>,
/// <c>subject:&lt;id&gt;</c>, <c>fact:&lt;id&gt;</c>, <c>component:&lt;id&gt;</c> or <c>&lt;section&gt;.&lt;key&gt;{placeholder}</c>.
/// Each was replaced by a neutral text so the screen still works.
/// </param>
public sealed record DebriefView(
    Grade Grade,
    string GradeLabel,
    int ScorePoints,
    double RawPoints,
    double FinalPoints,
    double CapPoints,
    string? CapNote,
    IReadOnlyList<DebriefDimensionView> Dimensions,
    IReadOnlyList<DebriefObjectiveView> Objectives,
    DebriefSPlusView SPlus,
    IReadOnlyList<DebriefDecisionView> BestDecisions,
    DebriefDecisionView? LargestAvoidableLoss,
    DebriefHistoryCardView HistoryCard,
    DebriefComparisonView Comparison,
    DebriefLabels Labels,
    string FictionalAdaptationText,
    IReadOnlyList<string> MissingKeys)
{
    /// <summary>Crew health at launch, worst and recovery, in the order the facts list them; empty when the facts carry none.</summary>
    public IReadOnlyList<DebriefCrewConditionView> CrewConditions { get; init; } = [];

    /// <summary>Incident, discovery and major decisions in MET order; empty when the facts carry none.</summary>
    public IReadOnlyList<DebriefTimelineEntryView> Timeline { get; init; } = [];

    /// <summary>Each astronaut lost in flight, in MET order; empty when everyone survived.</summary>
    public IReadOnlyList<DebriefCrewLossView> CrewLosses { get; init; } = [];

    /// <summary>Where and when the capsule came down; null when it never landed.</summary>
    public DebriefSplashdownView? Splashdown { get; init; }
}

/// <param name="MetText">MET of the death as HH:MM:SS.</param>
public sealed record DebriefCrewLossView(CrewId Crew, string Name, double MetS, string MetText);

/// <param name="ZoneName">The recovery zone's display name, or <see cref="DebriefLabels.OpenOcean"/> outside every zone.</param>
/// <param name="InZone">The capsule came down inside a recovery zone.</param>
public sealed record DebriefSplashdownView(double MetS, string MetText, string? ZoneId, string ZoneName, bool InZone);

public sealed record DebriefDimensionView(
    ScoreDimension Dimension,
    string Name,
    double Points,
    double MaxPoints,
    IReadOnlyList<DebriefScoreLineView> Lines,
    IReadOnlyList<DebriefNoteView> Notes);

public sealed record DebriefScoreLineView(string Key, string? Subject, string Text, double Points, double MaxPoints);

public sealed record DebriefNoteView(string Key, string? Subject, double? Value, string Text);

/// <param name="Explanation">The objectives-dimension text for this objective, e.g. "Dock with Agena: completed."</param>
/// <param name="CauseText">Who took the objective away (scenario or avoidable); null when it was not lost.</param>
/// <param name="ReasonText">What took it away (the feasibility rule); null when unknown or not lost.</param>
/// <param name="FinalMetText">MET of the last feasibility change as HH:MM:SS.</param>
public sealed record DebriefObjectiveView(
    ObjectiveId Id,
    string Title,
    ObjectiveFinalState State,
    string StateText,
    string Explanation,
    ObjectiveLossCause? Cause,
    string? CauseText,
    string? ReasonKey,
    string? ReasonText,
    bool CountedInDenominator,
    double Points,
    double MaxPoints,
    double? FinalMetS,
    string? FinalMetText);

public sealed record DebriefSPlusView(bool Achieved, SPlusPath Path, string Title, IReadOnlyList<DebriefNoteView> Reasons);

/// <param name="Magnitude">Weight of a good decision, or points an avoidable loss cost.</param>
public sealed record DebriefDecisionView(string Key, string? Subject, string Text, double Magnitude, double? MetS, string? MetText);

/// <param name="ImageSlotId">The briefing image slot the card shows; null when the card has a curated item of its own.</param>
/// <param name="ImageCaption">The card's own caption, else the briefing slot's.</param>
/// <param name="ImageCredit">The briefing slot's credit; null for a card's own item, which <paramref name="Credit"/> names.</param>
public sealed record DebriefHistoryCardView(
    string Label,
    string Title,
    string Text,
    string? ImageSlotId,
    string? ImageCaption,
    string? ImageAltText,
    string? ImageCredit,
    string? Credit)
{
    /// <summary>
    /// The curated item to show: the card's own or the slot's. Look it up by <c>itemId</c> in
    /// <c>content/nasa/manifest.json</c> for the cached media; null while nothing is picked.
    /// </summary>
    public string? NasaId { get; init; }
}

/// <param name="Label">The "Simulation result" provenance label.</param>
public sealed record DebriefComparisonView(string Label, string Intro, IReadOnlyList<DebriefComparisonLine> Lines, string Outro);

public sealed record DebriefComparisonLine(string Key, string Text);

/// <param name="RecoveryHealth">Null when the astronaut did not come home; <paramref name="RecoveryText"/> is then a dash.</param>
public sealed record DebriefCrewConditionView(
    CrewId Crew,
    string Name,
    double LaunchHealth,
    double WorstHealth,
    double? RecoveryHealth,
    string LaunchText,
    string WorstText,
    string RecoveryText)
{
    /// <summary>MET of the astronaut's death as HH:MM:SS; null for a survivor, or a loss the facts do not time.</summary>
    public string? LostMetText { get; init; }
}

/// <param name="KindText">Display name of <paramref name="Kind"/>, e.g. "Decision".</param>
/// <param name="KnownAtTheTime">What the ground knew at that moment (newest first, at most <see cref="DebriefComposer.MaxKnownItems"/>); empty for an incident.</param>
public sealed record DebriefTimelineEntryView(
    DebriefTimelineKind Kind,
    string KindText,
    string Key,
    string Text,
    double MetS,
    string MetText,
    IReadOnlyList<string> KnownAtTheTime);
