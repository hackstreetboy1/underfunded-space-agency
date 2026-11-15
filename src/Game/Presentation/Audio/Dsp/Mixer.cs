namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>Sums generators into one block with per-voice gain; finished voices are dropped.</summary>
public sealed class Mixer
{
    private readonly List<(ISoundGenerator Generator, float Gain)> _voices = [];
    private float[] _scratch = [];

    public int VoiceCount => _voices.Count;

    public void Add(ISoundGenerator generator, float gainDb = 0f)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _voices.Add((generator, Decibels.ToGain(gainDb)));
    }

    public void Render(Span<float> output)
    {
        output.Clear();
        if (_scratch.Length < output.Length) _scratch = new float[output.Length];
        var block = _scratch.AsSpan(0, output.Length);
        for (var v = _voices.Count - 1; v >= 0; v--)
        {
            var (generator, gain) = _voices[v];
            generator.Render(block);
            Signal.Add(output, block, gain);
            if (generator.IsFinished) _voices.RemoveAt(v);
        }
    }
}
