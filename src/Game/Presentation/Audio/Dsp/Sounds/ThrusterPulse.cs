namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>Short RCS pulse for the external view; the seed varies its colour slightly per pulse.</summary>
public sealed class ThrusterPulse(uint seed) : OneShotSound(seed)
{
    public override SoundId Id => SoundId.ThrusterPulse;

    protected override double DurationSeconds => 0.2;

    protected override float PeakDb => -10f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var noise = new NoiseSource(random.NextUInt());
        var hiss = new Biquad(FilterKind.BandPass, 2200.0 * random.Range(0.9f, 1.1f), 0.9);
        var air = new Biquad(FilterKind.LowPass, 600.0);
        var pop = new Oscillator(150.0 * random.Range(0.95f, 1.05f));
        var holdSeconds = random.Range(0.03f, 0.045f);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            var body = t < holdSeconds ? Shapes.Gate(t, 0.0, 1.0, 0.002) : (float)Math.Exp(-(t - holdSeconds) / 0.03);
            var white = noise.White();
            output[i] = (hiss.Process(white) * body)
                + (air.Process(white) * Shapes.AttackDecay(t, 0.002, 0.05) * 0.4f)
                + (pop.Sine() * Shapes.AttackDecay(t, 0.001, 0.018) * 0.5f);
        }
    }
}
