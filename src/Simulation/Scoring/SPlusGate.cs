namespace Usa.Simulation.Scoring;

/// <summary>
/// The S+ gate of GDD 10.3. Feasibility is game-determined: it reads only the recorded objective
/// states and causes, never the player's own view of them.
/// </summary>
public static class SPlusGate
{
    public const string PathAKey = "splus.path_a";
    public const string PathBKey = "splus.path_b";
    public const string CrewLostKey = "splus.blocked.crew_lost";
    public const string NotLandedKey = "splus.blocked.not_landed";
    public const string ComplicationKey = "splus.blocked.health_complication";
    public const string AbandonedKey = "splus.blocked.objective_abandoned";
    public const string AvoidableLossKey = "splus.blocked.objective_lost_avoidable";
    public const string ScoreBelowBandKey = "splus.blocked.score_below_band";
    public const string NoObjectivesKey = "splus.blocked.no_objectives";

    /// <summary>The gate alone; the score must also reach the S+ band (see <see cref="MissionScoring"/>).</summary>
    /// <param name="complicationThresholdPoints">Mission-caused loss above this per astronaut is a complication.</param>
    public static SPlusAssessment Evaluate(MissionRecord record, double complicationThresholdPoints)
    {
        ArgumentNullException.ThrowIfNull(record);
        var blocks = new List<ScoreNote>();

        foreach (var member in record.Crew)
        {
            if (!member.Survived)
            {
                blocks.Add(new ScoreNote(CrewLostKey, member.Crew.Value, null));
                continue;
            }

            var loss = record.MissionCausedLossPoints(member.Crew);
            if (loss > complicationThresholdPoints) blocks.Add(new ScoreNote(ComplicationKey, member.Crew.Value, loss));
        }

        if (!record.Return.Landed) blocks.Add(new ScoreNote(NotLandedKey, null, null));

        var scenarioLost = 0;
        var completed = 0;
        foreach (var objective in record.Objectives)
        {
            switch (ObjectiveRules.Classify(objective))
            {
                case ObjectiveClass.Abandoned:
                    blocks.Add(new ScoreNote(AbandonedKey, objective.Id.Value, null));
                    break;
                case ObjectiveClass.LostAvoidable:
                    blocks.Add(new ScoreNote(AvoidableLossKey, objective.Id.Value, null));
                    break;
                case ObjectiveClass.LostScenario:
                    scenarioLost++;
                    break;
                case ObjectiveClass.Completed:
                    completed++;
                    break;
            }
        }

        // S+ is earned through objectives: Path A needs a completed one, Path B a scenario abandonment.
        if (completed + scenarioLost == 0) blocks.Add(new ScoreNote(NoObjectivesKey, null, null));

        if (blocks.Count > 0) return new SPlusAssessment(false, SPlusPath.None, blocks);

        return scenarioLost == 0
            ? new SPlusAssessment(true, SPlusPath.A, [new ScoreNote(PathAKey, null, null)])
            : new SPlusAssessment(true, SPlusPath.B, [new ScoreNote(PathBKey, null, scenarioLost)]);
    }
}

/// <summary>Outcome of the gate before the score band is considered.</summary>
public sealed record SPlusAssessment(bool Eligible, SPlusPath Path, IReadOnlyList<ScoreNote> Reasons);
