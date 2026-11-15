using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session.Scripts;

/// <summary>
/// Determinism check for a played scenario: rebuilding the run from seed + command log must give the same 60 s hash sequence and
/// final state hash, through <see cref="MissionSession.Replay"/> and when the clock is advanced in any chunk size (the host's
/// time compression changes only how many ticks run per call).
/// </summary>
public static class ScenarioReplayCheck
{
    /// <summary>Chunk sizes covering one tick, an odd prime and a whole compressed frame at 240x (20 ticks/s × 240 / 60 fps).</summary>
    public static IReadOnlyList<int> DefaultChunks { get; } = [1, 997, 80];

    /// <returns>One line per mismatch; empty when every replay matches.</returns>
    public static IReadOnlyList<string> Verify(IDataSource source, ScenarioResult result, IReadOnlyList<int>? chunks = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);
        var problems = new List<string>();
        var script = result.Script;
        var end = new MissionTime(result.EndMetMs);

        var replay = MissionSession.Replay(source, script.Mission, script.Difficulty, script.Seed, result.Log, end);
        Compare("Replay()", replay, result, problems);

        foreach (var chunk in chunks ?? DefaultChunks)
        {
            var session = MissionSession.Load(source, script.Mission, script.Difficulty, script.Seed);
            foreach (var entry in result.Log.Entries)
            {
                RunTo(session, new MissionTime(entry.MetMs), chunk);
                session.Submit(entry.Request);
            }
            RunTo(session, end, chunk);
            Compare($"chunk {chunk}", session, result, problems);
        }
        return problems;
    }

    private static void RunTo(MissionSession session, MissionTime target, int chunk)
    {
        while (session.Now < target && session.Advance(target, chunk) > 0) session.DrainEvents(static _ => { });
    }

    private static void Compare(string label, MissionSession session, ScenarioResult result, List<string> problems)
    {
        var expected = result.Hashes;
        var actual = session.HashSamples;
        if (actual.Count != expected.Count)
        {
            problems.Add($"{label}: {actual.Count} hash samples, expected {expected.Count}");
            return;
        }
        for (var i = 0; i < expected.Count; i++)
        {
            if (actual[i] == expected[i]) continue;
            problems.Add($"{label}: hash differs first at {ScenarioPlayer.Met(expected[i].Time.Milliseconds)}");
            return;
        }
        if (session.StateHash() != result.FinalHash) problems.Add($"{label}: final state hash differs");
    }
}
