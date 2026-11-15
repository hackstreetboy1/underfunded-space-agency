namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>PCG32 (XSH-RR) so every procedural sound is reproducible from its seed.</summary>
public sealed class SynthRandom
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong Increment = 1442695040888963407UL;
    private ulong _state;

    public SynthRandom(uint seed)
    {
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        var old = _state;
        _state = (old * Multiplier) + Increment;
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16_777_216f);

    /// <summary>Uniform in [-1, 1).</summary>
    public float NextBipolar() => (NextFloat() * 2f) - 1f;

    public float Range(float min, float max) => min + ((max - min) * NextFloat());
}
