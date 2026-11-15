namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>Soft radio-band hiss with slow flutter and sparse crackles, under transmissions.</summary>
public sealed class RadioStaticBed(float[] table, float intensity = 1f)
    : LoopSound(table, intensity, fadeInSeconds: 0.06, fadeOutSeconds: 0.12)
{
    private const double CracklesPerSecond = 5.0;

    public override SoundId Id => SoundId.RadioStaticBed;

    public static float[] BuildTable(uint seed)
    {
        var noise = new NoiseSource(seed);
        var random = new SynthRandom(seed + 1);
        var flutter = new Flutter(seed + 2, 3.0);
        var highPass = new Biquad(FilterKind.HighPass, 350.0);
        var lowPass = new Biquad(FilterKind.LowPass, 3200.0);
        var crackleLevel = 0f;
        var crackleDecay = 0f;
        var table = BuildNoiseLayer(TableLength(PreviewSeconds), () =>
        {
            crackleLevel *= crackleDecay;
            if (random.NextFloat() < CracklesPerSecond / AudioFormat.SampleRate)
            {
                crackleLevel = random.Range(0.4f, 1.2f);
                crackleDecay = (float)Math.Exp(-1.0 / (random.Range(0.0006f, 0.0025f) * AudioFormat.SampleRate));
            }

            var sample = (noise.White() * 0.6f) + (noise.White() * crackleLevel);
            return lowPass.Process(highPass.Process(sample)) * (1f + (0.35f * flutter.Next()));
        });
        Signal.NormalizeRms(table, -32f, -14f);
        return table;
    }
}
