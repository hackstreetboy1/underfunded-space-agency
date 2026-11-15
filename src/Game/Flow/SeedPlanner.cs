using Usa.Simulation.Kernel;

namespace Usa.Game.Flow;

/// <summary>
/// Scenario seeds for the front end (GDD 10.4). A first run of a (mission, difficulty) always gets the same seed,
/// Replay reuses the seed of the run just played, and Retry steps a deterministic sequence to a new variant.
/// </summary>
public static class SeedPlanner
{
    public static ulong Initial(string missionId, string difficulty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(difficulty);
        return Mix(StableHash.Fnv1a64($"{missionId}/{difficulty}"));
    }

    public static ulong Replay(ulong seed) => seed;

    public static ulong Retry(ulong seed)
    {
        var next = Mix(seed);
        return next == seed ? next + 1 : next;
    }

    /// <summary>SplitMix64 finaliser: a bijection, so distinct inputs never share a seed.</summary>
    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
