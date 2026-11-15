using Usa.Simulation.Commands;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Diagnostics.Repro;

/// <summary>How one recorded hash sample compares with the replay.</summary>
public enum HashSampleStatus
{
    Match,
    Different,
    MissingInReplay,
    ExtraInReplay,
}

public sealed record HashSampleComparison(long MetMs, HashSampleStatus Status, string? Recorded, string? Replayed);

/// <summary>
/// The result of replaying a repro: every 60 s sample in MET order, the first one that does not match (if any), the last
/// matching MET before it, and the logged commands submitted between the two (the likeliest cause).
/// </summary>
public sealed record ReproReplayReport(
    ReproMission Mission,
    IReadOnlyList<HashSampleComparison> Samples,
    HashSampleComparison? FirstMismatch,
    long? LastMatchMetMs,
    IReadOnlyList<CommandLogEntry> CommandsBeforeMismatch,
    string ReplayedFinalHash,
    long ReplayedMetMs)
{
    public bool FinalHashMatches => ReplayedFinalHash == Mission.FinalHash && ReplayedMetMs == Mission.MetMs;

    public bool Matches => FirstMismatch is null && FinalHashMatches;

    public int MatchingSamples => Samples.Count(s => s.Status == HashSampleStatus.Match);
}

/// <summary>
/// Rebuilds a repro's run headless (<see cref="MissionSession.Replay"/> from seed + command log, to the captured MET) and
/// compares its hash sequence with the recorded one. Logged commands keep their run-tagged ids, so the replay needs no run tag.
/// </summary>
public static class ReproReplayer
{
    /// <exception cref="InvalidOperationException">The repro has no mission or no command log.</exception>
    /// <exception cref="DefinitionException">The data no longer loads this mission.</exception>
    public static ReproReplayReport Replay(IDataSource source, ReproFile repro, int chunkTicks = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(repro);
        var mission = repro.Document.Mission ?? throw new InvalidOperationException("The repro was captured without a running mission; there is nothing to replay.");
        var log = repro.CommandLog ?? throw new InvalidOperationException("The repro has no command log.");

        var session = MissionSession.Replay(source, mission.MissionId, mission.Difficulty, mission.Seed, log, new MissionTime(mission.MetMs), chunkTicks);
        var replayed = new ReproHash[session.HashSamples.Count];
        for (var i = 0; i < replayed.Length; i++) replayed[i] = new ReproHash(session.HashSamples[i].Time.Milliseconds, session.HashSamples[i].Hash);
        return Compare(mission, repro.Document.Hashes, replayed, log, session.StateHash(), session.Now.Milliseconds);
    }

    /// <summary>Pairs recorded and replayed samples by MET (both are in MET order) and finds the first mismatch.</summary>
    public static ReproReplayReport Compare(
        ReproMission mission,
        IReadOnlyList<ReproHash> recorded,
        IReadOnlyList<ReproHash> replayed,
        CommandLog log,
        string replayedFinalHash,
        long replayedMetMs)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(replayed);
        ArgumentNullException.ThrowIfNull(log);

        var samples = new List<HashSampleComparison>(Math.Max(recorded.Count, replayed.Count));
        int r = 0, p = 0;
        while (r < recorded.Count || p < replayed.Count)
        {
            if (p >= replayed.Count || (r < recorded.Count && recorded[r].MetMs < replayed[p].MetMs))
            {
                samples.Add(new(recorded[r].MetMs, HashSampleStatus.MissingInReplay, recorded[r].Hash, null));
                r++;
            }
            else if (r >= recorded.Count || replayed[p].MetMs < recorded[r].MetMs)
            {
                samples.Add(new(replayed[p].MetMs, HashSampleStatus.ExtraInReplay, null, replayed[p].Hash));
                p++;
            }
            else
            {
                var status = recorded[r].Hash == replayed[p].Hash ? HashSampleStatus.Match : HashSampleStatus.Different;
                samples.Add(new(recorded[r].MetMs, status, recorded[r].Hash, replayed[p].Hash));
                r++;
                p++;
            }
        }

        HashSampleComparison? first = null;
        long? lastMatch = null;
        foreach (var sample in samples)
        {
            if (sample.Status == HashSampleStatus.Match)
            {
                lastMatch = sample.MetMs;
                continue;
            }
            first = sample;
            break;
        }

        // A sample at MET t is taken by the tick that reaches t, before anything is submitted at t; so the commands that can
        // explain a bad sample were submitted in [last match, t). Without a bad sample the final hash (taken after the
        // submissions at the capture MET) is the only evidence, and the window closes at the capture, inclusive.
        var commands = new List<CommandLogEntry>();
        var finalDiffers = replayedFinalHash != mission.FinalHash;
        if (first is not null || finalDiffers)
        {
            var start = lastMatch ?? long.MinValue;
            foreach (var entry in log.Entries)
            {
                var inWindow = first is { } bad ? entry.MetMs < bad.MetMs : entry.MetMs <= mission.MetMs;
                if (entry.MetMs >= start && inWindow) commands.Add(entry);
            }
        }
        return new ReproReplayReport(mission, samples, first, lastMatch, commands, replayedFinalHash, replayedMetMs);
    }
}
