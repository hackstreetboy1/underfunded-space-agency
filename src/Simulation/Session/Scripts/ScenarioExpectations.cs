using System.Globalization;
using Usa.Simulation.Ports;
using Usa.Simulation.Scoring;

namespace Usa.Simulation.Session.Scripts;

/// <summary>Checks a played scenario against its <see cref="ScenarioExpectation"/>; every mismatch is one failure line.</summary>
internal static class ScenarioExpectations
{
    public static void Check(ScenarioResult result, IStateQuery state, IExpressionCompiler compiler, List<string> failures)
    {
        var expect = result.Script.Expect;
        var score = result.Score;
        if (expect.Ended is { } ended && ended != result.Ended) failures.Add($"ended: expected {ended}, got {result.Ended}");
        if (expect.Grade is { } grade && grade != score.GradeLabel) failures.Add($"grade: expected {grade}, got {score.GradeLabel} ({Points(score)})");
        if (expect.Grades is { Count: > 0 } grades && !grades.Contains(score.GradeLabel))
            failures.Add($"grade: expected one of {string.Join("/", grades)}, got {score.GradeLabel} ({Points(score)})");
        if (expect.SPlusPath is { } path && !string.Equals(path, PathText(score), StringComparison.OrdinalIgnoreCase))
            failures.Add($"sPlusPath: expected {path}, got {PathText(score)} ({string.Join(", ", score.SPlus.Reasons.Select(r => r.Key + (r.Subject is null ? "" : ":" + r.Subject)))})");
        if (expect.Survivors is { } survivors && survivors != result.Survivors) failures.Add($"survivors: expected {survivors}, got {result.Survivors}");
        if (expect.MinScore is { } min && score.FinalPoints < min) failures.Add($"score: expected >= {min}, got {Points(score)}");
        if (expect.MaxScore is { } max && score.FinalPoints > max) failures.Add($"score: expected <= {max}, got {Points(score)}");
        if (expect.Zone is { } zone && zone != result.Metrics.LandingZone) failures.Add($"zone: expected {zone}, got {result.Metrics.LandingZone}");

        if (expect.Objectives is { } objectives)
        {
            foreach (var (id, expected) in objectives)
            {
                var actual = state.TryGet($"mission.objective.{id}.state", out var value) ? value.AsText() : "<unknown objective>";
                if (actual != expected) failures.Add($"objective {id}: expected {expected}, got {actual}");
            }
        }
        foreach (var key in expect.Events)
        {
            if (!result.EventKeys.ContainsKey(key)) failures.Add($"event {key} did not occur");
        }
        foreach (var key in expect.NotEvents)
        {
            if (result.EventKeys.TryGetValue(key, out var at)) failures.Add($"event {key} occurred at {ScenarioPlayer.Met(at)}");
        }
        foreach (var check in expect.Checks)
        {
            try
            {
                if (!compiler.Compile(check).Evaluate(state).AsBool()) failures.Add($"check failed: {check}");
            }
            catch (Exception ex) when (ex is ExpressionException or InvalidOperationException or UnknownStatePathException)
            {
                failures.Add($"check {check}: {ex.Message}");
            }
        }
    }

    public static string PathText(ScoreBreakdown score) =>
        score.SPlus.Achieved ? score.SPlus.Path.ToString() : "none";

    private static string Points(ScoreBreakdown score) => score.FinalPoints.ToString("0.0", CultureInfo.InvariantCulture);
}
