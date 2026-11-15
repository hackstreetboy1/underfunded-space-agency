namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>
/// Entry roar: a wideband buffeting noise table shaped in real time. Intensity opens a 4-pole
/// low-pass from 220 Hz to ~3 kHz and raises the level, so peak heating sounds bright and loud
/// while the early and late phases are a muffled rumble.
/// </summary>
public sealed class EntryRoar(float[] table, float intensity = 1f)
    : LoopSound(table, intensity, fadeInSeconds: 0.5, fadeOutSeconds: 1.5)
{
    private const double MinCutoffHz = 220.0;
    private const double CutoffRange = 14.0;
    private const int CoefficientInterval = 32;
    private readonly Biquad _first = new(FilterKind.LowPass, CutoffFor(intensity), 0.6);
    private readonly Biquad _second = new(FilterKind.LowPass, CutoffFor(intensity), 0.6);
    private float _configuredIntensity = intensity;
    private int _countdown;

    public override SoundId Id => SoundId.EntryRoar;

    public static double CutoffFor(float intensity) => MinCutoffHz * Math.Pow(CutoffRange, Math.Clamp(intensity, 0f, 1f));

    public static float[] BuildTable(uint seed)
    {
        var noise = new NoiseSource(seed);
        var buffet = new Flutter(seed + 1, 5.0);
        var swell = new Flutter(seed + 2, 0.3);
        var rumble = new Biquad(FilterKind.LowPass, 120.0);
        var subCut = new Biquad(FilterKind.HighPass, 40.0);
        var table = BuildNoiseLayer(TableLength(PreviewSeconds), () =>
        {
            var sample = subCut.Process(noise.Pink() + (rumble.Process(noise.Brown()) * 0.3f));
            return sample * (1f + (0.35f * buffet.Next())) * (1f + (0.1f * swell.Next()));
        });
        Signal.NormalizeRms(table, -14f, -2f);
        return table;
    }

    protected override float Shape(float sample, float intensity)
    {
        if (--_countdown <= 0)
        {
            _countdown = CoefficientInterval;
            if (Math.Abs(intensity - _configuredIntensity) > 1e-4f)
            {
                _configuredIntensity = intensity;
                var cutoff = CutoffFor(intensity);
                _first.Configure(FilterKind.LowPass, cutoff, 0.6);
                _second.Configure(FilterKind.LowPass, cutoff, 0.6);
            }
        }

        return _second.Process(_first.Process(sample)) * MathF.Pow(intensity, 1.3f);
    }
}
