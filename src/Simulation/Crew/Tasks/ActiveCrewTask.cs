using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>
/// The task in the crew slot. Work progresses linearly; when the assignee's band or rate impairment
/// changes the remaining fraction is re-timed from <see cref="SegmentStart"/> at the new duration.
/// </summary>
/// <param name="DurationS">Full task duration under the current conditions.</param>
/// <param name="SegmentProgress">Fraction of work done at <paramref name="SegmentStart"/>.</param>
/// <param name="PlannedBand">Assignee band the current timing was computed for.</param>
/// <param name="PlannedRateImpaired">Assignee rate impairment the current timing was computed for.</param>
public sealed record ActiveCrewTask(
    BoundTask Task,
    CrewId Assignee,
    CommandId Origin,
    MissionTime StartedAt,
    MissionTime EndsAt,
    double DurationS,
    double SegmentProgress,
    MissionTime SegmentStart,
    HealthBand PlannedBand,
    bool PlannedRateImpaired)
{
    /// <summary>Fraction of the work done at <paramref name="now"/>, 0-1.</summary>
    public double ProgressAt(MissionTime now)
    {
        var elapsedS = (now.Milliseconds - SegmentStart.Milliseconds) / 1000.0;
        return Math.Clamp(SegmentProgress + elapsedS / DurationS, 0.0, 1.0);
    }
}
