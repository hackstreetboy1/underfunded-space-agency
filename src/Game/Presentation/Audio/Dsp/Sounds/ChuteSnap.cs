namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>Mortar crack and riser snap, a low thump, then canopy flutter that dies away.</summary>
public sealed class ChuteSnap(uint seed) : OneShotSound(seed)
{
    public override SoundId Id => SoundId.ChuteSnap;

    protected override double DurationSeconds => 1.3;

    protected override float PeakDb => -6f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var noise = new NoiseSource(random.NextUInt());
        var crack = new Biquad(FilterKind.HighPass, 1500.0);
        var canopy = new Biquad(FilterKind.BandPass, 650.0, 0.9);
        var thump = new Oscillator();
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            thump.FrequencyHz = 45.0 + (50.0 * Math.Exp(-t / 0.06));
            var crackEnvelope = Shapes.AttackDecay(t, 0.0004, 0.010) + (0.5f * Shapes.AttackDecay(t - 0.035, 0.0004, 0.008));
            var flutter = 0.55f + (0.45f * (float)Math.Sin(2.0 * Math.PI * 13.0 * t));
            output[i] = (crack.Process(noise.White()) * crackEnvelope)
                + (thump.Sine() * Shapes.AttackDecay(t - 0.004, 0.003, 0.09) * 0.7f)
                + (canopy.Process(noise.Pink()) * Shapes.AttackDecay(t - 0.03, 0.02, 0.35) * flutter * 0.45f);
        }
    }
}
