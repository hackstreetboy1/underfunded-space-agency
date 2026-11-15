namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

public sealed class UiTick(uint seed) : OneShotSound(seed)
{
    public override SoundId Id => SoundId.UiTick;

    protected override double DurationSeconds => 0.045;

    protected override float PeakDb => -20f;

    protected override double FadeOutSeconds => 0.005;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        // Slight pitch variation keeps rapid repeated ticks from sounding machine-gunned.
        var tone = new Oscillator(1900.0 * random.Range(0.98f, 1.02f));
        var noise = new NoiseSource(random.NextUInt());
        var click = new Biquad(FilterKind.HighPass, 3000.0);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            output[i] = (tone.Sine() * Shapes.AttackDecay(t, 0.0005, 0.006))
                + (click.Process(noise.White()) * Shapes.AttackDecay(t, 0.0002, 0.0008) * 0.4f);
        }
    }
}
