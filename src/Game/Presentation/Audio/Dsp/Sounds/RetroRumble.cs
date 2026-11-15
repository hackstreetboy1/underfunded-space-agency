namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>
/// Retro-rocket burn: low rumble with rough combustion modulation, lightly saturated, plus a
/// 420 Hz body so it still reads on small speakers.
/// </summary>
public sealed class RetroRumble(float[] table, float intensity = 1f)
    : LoopSound(table, intensity, fadeInSeconds: 0.15, fadeOutSeconds: 0.8)
{
    private const float Drive = 1.5f;

    public override SoundId Id => SoundId.RetroRumble;

    public static float[] BuildTable(uint seed)
    {
        var noise = new NoiseSource(seed);
        var roughness = new Flutter(seed + 1, 9.0);
        var surge = new Flutter(seed + 2, 0.7);
        var rumble = new Biquad(FilterKind.LowPass, 160.0);
        var body = new Biquad(FilterKind.BandPass, 420.0, 0.8);
        var subCut = new Biquad(FilterKind.HighPass, 38.0);
        var saturationNorm = 1f / MathF.Tanh(Drive);
        var table = BuildNoiseLayer(TableLength(PreviewSeconds), () =>
        {
            var sample = subCut.Process((rumble.Process(noise.Brown()) * 1.2f) + (body.Process(noise.Pink()) * 0.9f));
            sample *= (1f + (0.4f * roughness.Next())) * (1f + (0.15f * surge.Next()));
            return MathF.Tanh(Drive * sample) * saturationNorm;
        });
        Signal.NormalizeRms(table, -18f, -4f);
        return table;
    }
}
