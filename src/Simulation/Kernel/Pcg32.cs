namespace Usa.Simulation.Kernel;

/// <summary>PCG-XSH-RR 32-bit generator (O'Neill 2014). Deterministic across platforms and runs.</summary>
/// <remarks>
/// Every consumer gets its own named stream (<see cref="ForStream"/>) so adding a consumer never
/// shifts the sequence another one sees.
/// </remarks>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;
    private readonly ulong _increment;
    private ulong _state;

    public Pcg32(ulong seed, ulong stream)
    {
        _increment = (stream << 1) | 1UL;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    public static Pcg32 ForStream(ulong scenarioSeed, string streamName) => new(scenarioSeed, StableHash.Fnv1a64(streamName));

    /// <summary>Internal state for hashing and save/restore.</summary>
    public ulong State => _state;

    public uint NextUInt()
    {
        var old = _state;
        _state = unchecked(old * Multiplier + _increment);
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

    /// <summary>Uniform in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform integer in [0, <paramref name="exclusiveMax"/>) without modulo bias.</summary>
    public int NextInt(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        var bound = (uint)exclusiveMax;
        var threshold = unchecked((uint)-(int)bound) % bound;
        while (true)
        {
            var value = NextUInt();
            if (value >= threshold) return (int)(value % bound);
        }
    }

    public bool Chance(double probability) => NextDouble() < probability;
}

/// <summary>Hashes that are stable across processes (unlike <see cref="string.GetHashCode()"/>).</summary>
public static class StableHash
{
    public static ulong Fnv1a64(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text) hash = unchecked((hash ^ c) * 1099511628211UL);
        return hash;
    }
}
