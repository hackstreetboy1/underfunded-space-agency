namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

public sealed class PttOpen(uint seed) : OneShotSound(seed)
{
    public override SoundId Id => SoundId.PttOpen;

    protected override double DurationSeconds => 0.2;

    protected override float PeakDb => -16f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var tone = new Oscillator(2525.0);
        var noise = new NoiseSource(random.NextUInt());
        var click = new Biquad(FilterKind.HighPass, 2500.0);
        var squelch = new Biquad(FilterKind.BandPass, 1800.0, 0.8);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            var white = noise.White();
            output[i] = (tone.Sine() * Shapes.Gate(t, 0.008, 0.15, 0.006))
                + (click.Process(white) * Shapes.AttackDecay(t, 0.0003, 0.0015) * 0.8f)
                + (squelch.Process(white) * Shapes.AttackDecay(t, 0.002, 0.03) * 0.35f);
        }
    }
}
