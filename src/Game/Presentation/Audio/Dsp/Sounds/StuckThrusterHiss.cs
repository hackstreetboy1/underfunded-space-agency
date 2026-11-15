namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>A thruster that will not close: fluttering high hiss, pressure sputter and a faint whistle.</summary>
public sealed class StuckThrusterHiss(float[] table, float intensity = 1f)
    : LoopSound(table, intensity, fadeInSeconds: 0.08, fadeOutSeconds: 0.3)
{
    public override SoundId Id => SoundId.StuckThrusterHiss;

    public static float[] BuildTable(uint seed)
    {
        var length = TableLength(PreviewSeconds);
        var noise = new NoiseSource(seed);
        var flutter = new Flutter(seed + 1, 14.0);
        var sputter = new Flutter(seed + 2, 1.2);
        var highPass = new Biquad(FilterKind.HighPass, 1800.0);
        var band = new Biquad(FilterKind.BandPass, 3000.0, 0.7);
        var soften = new Biquad(FilterKind.LowPass, 6500.0);
        var table = BuildNoiseLayer(length, () =>
        {
            var pressure = 0.875f + (0.125f * sputter.Next());
            return soften.Process(band.Process(highPass.Process(noise.White()))) * (1f + (0.25f * flutter.Next())) * pressure;
        });
        Signal.NormalizeRms(table, -26f, -10f);

        // 2750 Hz completes whole cycles in 8 s, so the whistle needs no crossfade.
        var whistle = new Oscillator(2750.0);
        var whistleGain = Decibels.ToGain(-50f);
        for (var i = 0; i < length; i++)
        {
            table[i] += whistle.Sine() * whistleGain;
        }

        return table;
    }

    protected override float Shape(float sample, float intensity) => sample * Shapes.Lerp(0.25f, 1f, intensity);
}
