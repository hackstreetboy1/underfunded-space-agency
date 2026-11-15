using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Guidance;

/// <summary>Contents of <c>missions/&lt;id&gt;/hints.json</c> (schema <c>hints/1</c>): the authored hint tree.</summary>
/// <param name="CooldownS">Minimum mission time between unsolicited tips.</param>
public sealed record HintsFile(
    string Schema,
    string MissionId,
    double CooldownS,
    StuckTuning Stuck,
    IReadOnlyList<HintProblemData> Problems) : IVersionedDefinition
{
    public const string SchemaTag = "hints/1";
}

/// <param name="Priority">Higher wins when several problems are open; only the top one gets tips.</param>
/// <param name="Activation">Expression that opens the problem.</param>
/// <param name="Resolution">Expression that retires the problem for good.</param>
/// <param name="Stages">Exactly five, nudge to direct.</param>
public sealed record HintProblemData(
    string Id,
    int Priority,
    string Activation,
    string Resolution,
    IReadOnlyList<HintStageData> Stages);

/// <param name="Alternatives">Variant wording, first matching <c>when</c> wins (e.g. which RCS ring is healthy).</param>
public sealed record HintStageData(
    HintStage Stage,
    string Text,
    IReadOnlyList<FactId> FactRefs,
    IReadOnlyList<ActionId> ActionRefs,
    IReadOnlyList<HintAlternativeData>? Alternatives = null);

/// <summary>Replaces the stage's text and, when given, its references.</summary>
public sealed record HintAlternativeData(
    string When,
    string Text,
    IReadOnlyList<FactId>? FactRefs = null,
    IReadOnlyList<ActionId>? ActionRefs = null);

/// <summary>How the stuck score (in stuck-seconds) moves; see <see cref="StuckTracker"/>.</summary>
/// <param name="FailedActionPenaltyS">Added per failed or invalid action.</param>
/// <param name="CriticalAlarmExtraRate">Extra stuck-seconds per second while a critical alarm is unresolved.</param>
/// <param name="NoNewInfoAfterS">Seconds without new diagnostic information before the extra rate applies.</param>
/// <param name="NoNewInfoExtraRate">Extra stuck-seconds per second once no new information has arrived for a while.</param>
/// <param name="DiagnosticReliefS">Removed on a meaningful diagnostic for the problem.</param>
/// <param name="RepairReliefS">Removed on a repair.</param>
/// <param name="StabilizationReliefS">Removed on a stabilization.</param>
public sealed record StuckTuning(
    double FailedActionPenaltyS,
    double CriticalAlarmExtraRate,
    double NoNewInfoAfterS,
    double NoNewInfoExtraRate,
    double DiagnosticReliefS,
    double RepairReliefS,
    double StabilizationReliefS);
