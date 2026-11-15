namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>
/// Cabin bed: two fans (118 and 119.5 Hz, beating at 1.5 Hz), a faint 400 Hz inverter whine and
/// low-passed ventilation air. Tone frequencies are multiples of 1/8 Hz, so the tonal layer is
/// exactly periodic over the 8 s table and needs no crossfade.
/// </summary>
public sealed class CabinHumVentBed(float[] table, float intensity = 1f)
    : LoopSound(table, intensity, fadeInSeconds: 1.0, fadeOutSeconds: 1.5)
{
    private static readonly float[] FanPartials = [0.45f, 0.2f, 0.08f];
    private static readonly float[] InverterPartials = [0.4f, 0.25f];

    public override SoundId Id => SoundId.CabinHumVentBed;

    public static float[] BuildTable(uint seed)
    {
        var length = TableLength(PreviewSeconds);
        var noise = new NoiseSource(seed);
        var breathing = new Flutter(seed + 1, 0.4);
        var airLow = new Biquad(FilterKind.HighPass, 80.0);
        var airHigh = new Biquad(FilterKind.LowPass, 1100.0, 0.6);
        var hissBand = new Biquad(FilterKind.BandPass, 2500.0, 0.4);
        var air = BuildNoiseLayer(length, () =>
        {
            var body = airHigh.Process(airLow.Process(noise.Pink()));
            return (body + (hissBand.Process(noise.White()) * 0.08f)) * (1f + (0.2f * breathing.Next()));
        });
        Signal.NormalizeRms(air, -32f, -12f);

        var hum = new float[length];
        var fanA = new Oscillator(118.0);
        var fanB = new Oscillator(119.5);
        var inverter = new Oscillator(400.0);
        for (var i = 0; i < length; i++)
        {
            hum[i] = fanA.Additive(FanPartials) + (0.6f * fanB.Additive(FanPartials)) + (0.12f * inverter.Additive(InverterPartials));
        }

        Signal.NormalizeRms(hum, -38f, -18f);
        Signal.Add(air, hum);
        Signal.NormalizeRms(air, -30f, -12f);
        return air;
    }
}
