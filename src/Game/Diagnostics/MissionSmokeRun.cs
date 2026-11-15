using System.Diagnostics;
using Usa.Game.Integration;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Diagnostics;

/// <summary>What one headless mission run produced.</summary>
/// <param name="Failures">Port contract violations seen during the run (empty when none).</param>
public sealed record MissionSmokeResult(
    MissionLaunchRequest Request,
    MissionTime Start,
    MissionTime End,
    IReadOnlyList<StateHashSample> Hashes,
    string FinalHash,
    int EventCount,
    TimeSpan Elapsed,
    IReadOnlyList<string> Failures);

/// <summary>
/// Runs a loaded mission headless through <see cref="SessionPortAdapter"/> the way the simulation host does: whole ticks in
/// fixed chunks, compression inputs read between chunks, events drained as it goes.
/// </summary>
public static class MissionSmokeRun
{
    public static MissionSmokeResult Run(LoadedMission mission, long durationMs, int ticksPerCall)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentOutOfRangeException.ThrowIfLessThan(durationMs, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerCall, 1);

        var port = mission.Port;
        var failures = new List<string>();
        var events = 0;
        void Count(ISimEvent _) => events++;

        var watch = Stopwatch.StartNew();
        var start = port.Now;
        var end = start + durationMs;
        while (port.Now < end)
        {
            var ran = port.Advance(end, ticksPerCall);
            var inputs = port.ReadCompressionInputs();
            if (inputs.Now != port.Now) failures.Add($"compression inputs at {inputs.Now}, session at {port.Now}");
            if (inputs.SecondsToNextEventMet is { } next && !(next > 0)) failures.Add($"next event {next} s is not ahead");
            port.DrainEvents(Count);
            if (ran == 0)
            {
                failures.Add($"session stopped advancing at {port.Now} (frozen: {port.IsFrozen})");
                break;
            }
        }
        watch.Stop();

        return new MissionSmokeResult(
            mission.Request, start, port.Now, [.. port.Session.HashSamples], port.Session.StateHash(), events, watch.Elapsed, failures);
    }
}
