namespace Usa.Game.Presentation.CrewFeed;

/// <summary>Shape-key weights, each 0..1. The mapper reuses one instance per crew member, so read it each frame.</summary>
public sealed class FaceWeights
{
    private readonly float[] _weights = new float[ShapeKeys.Count];

    public float this[ShapeKey key]
    {
        get => _weights[(int)key];
        internal set => _weights[(int)key] = Math.Clamp(value, 0f, 1f);
    }

    public void CopyTo(Span<float> destination) => _weights.CopyTo(destination);

    internal void Clear() => Array.Clear(_weights);
}
