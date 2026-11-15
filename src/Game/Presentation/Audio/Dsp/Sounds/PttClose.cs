namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

public sealed class PttClose(uint seed) : OneShotSound(seed)
{
    public override SoundId Id => SoundId.PttClose;

    protected override double DurationSeconds => 0.32;

    protected override float PeakDb => -16f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var tone = new Oscillator(2475.0);
        var noise = new NoiseSource(random.NextUInt());
        var squelch = new Biquad(FilterKind.BandPass, 2200.0, 0.6);
        var click = new Biquad(FilterKind.HighPass, 2500.0);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            var white = noise.White();
            output[i] = (tone.Sine() * Shapes.Gate(t, 0.0, 0.12, 0.006))
                + (squelch.Process(white) * Shapes.AttackDecay(t - 0.11, 0.005, 0.045) * 0.6f)
                + (click.Process(white) * Shapes.AttackDecay(t - 0.235, 0.0003, 0.0012) * 0.5f);
        }
    }
}
