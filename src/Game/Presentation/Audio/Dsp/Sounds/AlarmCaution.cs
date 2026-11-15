namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>Descending two-tone chime (G5 then D5), played once per caution.</summary>
public sealed class AlarmCaution(uint seed) : OneShotSound(seed)
{
    private static readonly float[] Partials = [0.3f, 0f, 0.1f];

    public override SoundId Id => SoundId.AlarmCaution;

    protected override double DurationSeconds => 1.0;

    protected override float PeakDb => -9f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var high = new Oscillator(783.99);
        var low = new Oscillator(587.33);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            output[i] = (high.Additive(Partials) * Note(t, 0.0))
                + (low.Additive(Partials) * Note(t, 0.3));
        }
    }

    private static float Note(double t, double start) =>
        Shapes.AttackDecay(t - start, 0.004, 0.22) * Shapes.Gate(t, start, 0.68, 0.02);
}
