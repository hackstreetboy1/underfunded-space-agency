namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

public sealed class UiConfirm(uint seed) : OneShotSound(seed)
{
    private static readonly float[] Partials = [0.15f];

    public override SoundId Id => SoundId.UiConfirm;

    protected override double DurationSeconds => 0.22;

    protected override float PeakDb => -16f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var first = new Oscillator(660.0);
        var second = new Oscillator(990.0);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            output[i] = (first.Additive(Partials) * Blip(t, 0.0)) + (second.Additive(Partials) * Blip(t, 0.075));
        }
    }

    private static float Blip(double t, double start) =>
        Shapes.AttackDecay(t - start, 0.003, 0.04) * Shapes.Gate(t, start, 0.14, 0.01);
}
