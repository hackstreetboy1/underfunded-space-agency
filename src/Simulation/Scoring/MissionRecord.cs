using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scoring;

/// <summary>
/// Everything the grade is computed from, filled by the integration step when the mission ends.
/// Hints and tips are deliberately absent: guidance can never influence the grade (GDD D13).
/// </summary>
public sealed record MissionRecord(
    IReadOnlyList<CrewHealthRecord> Crew,
    IReadOnlyList<HealthLossRecord> HealthLosses,
    ReturnRecord Return,
    IReadOnlyList<ObjectiveRecord> Objectives,
    VehicleConditionRecord Vehicle,
    DecisionMetrics Decisions)
{
    public int CrewCount => Crew.Count;

    public int SurvivorCount
    {
        get
        {
            var survivors = 0;
            foreach (var member in Crew)
            {
                if (member.Survived) survivors++;
            }
            return survivors;
        }
    }

    /// <summary>Mission-caused health loss of one crew member, summed over every cause.</summary>
    public double MissionCausedLossPoints(CrewId crew)
    {
        var total = 0.0;
        foreach (var loss in HealthLosses)
        {
            if (loss.Crew == crew) total += loss.LossPoints;
        }
        return total;
    }
}

/// <summary>Health of one astronaut on the 0-100 scale.</summary>
public sealed record CrewHealthRecord(CrewId Crew, bool Survived, double HealthAtLaunch, double WorstHealth, double HealthAtEnd);

/// <summary>Health lost to one mission condition, e.g. cause <c>spin_vestibular</c> or <c>entry_g</c>.</summary>
public sealed record HealthLossRecord(CrewId Crew, string Cause, double LossPoints);

/// <param name="RecoveryZoneId">Flight recovery zone id, or null when the capsule came down outside every zone.</param>
public sealed record ReturnRecord(
    bool Landed,
    string? RecoveryZoneId,
    double RecoveryWaitHours,
    double EntryPeakG,
    bool EquipmentJettisonedBeforeRetrofire,
    bool ChutesDeployedInLimits);

/// <param name="WeightShare">
/// Share of the objectives dimension (0-1), e.g. 0.3 for O1: Scenario's <c>ObjectiveOutcome.WeightPct</c> (30) divided by 100.
/// </param>
/// <param name="Cause">Why the objective was lost; required for impossible and unacceptable_risk.</param>
/// <param name="Transitions">Feasibility history with MET, for the debrief.</param>
public sealed record ObjectiveRecord(
    ObjectiveId Id,
    double WeightShare,
    ObjectiveFinalState FinalState,
    ObjectiveLossCause? Cause,
    IReadOnlyList<ObjectiveTransition> Transitions);

public sealed record ObjectiveTransition(double MetS, ObjectiveFeasibility State, ObjectiveLossCause? Cause);

/// <param name="CriticalSystemsTotal">Critical systems the vehicle launched with.</param>
/// <param name="UncontainedDamageCount">Damage events that spread beyond the initial fault.</param>
public sealed record VehicleConditionRecord(
    int CriticalSystemsTotal,
    int CriticalSystemsRemaining,
    RedundancyStatus Redundancy,
    int UncontainedDamageCount);

public sealed record DecisionMetrics(
    double AvoidablePropellantWasteKg,
    double AvoidableRiskExposureS,
    IReadOnlyList<WeightedDecision> AvoidableLosses,
    IReadOnlyList<WeightedDecision> GoodDecisions);

/// <summary>A notable decision with an explanation key; the weight (0-1) ranks it and, for losses, sets its penalty.</summary>
public sealed record WeightedDecision(string Key, double Weight, double? MetS);
