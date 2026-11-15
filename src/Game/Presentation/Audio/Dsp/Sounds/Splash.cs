namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>Splashdown: low impact, a wash of spray, then a few rising bubbles.</summary>
public sealed class Splash(uint seed) : OneShotSound(seed)
{
    private const int BubbleCount = 14;
    private const double BubbleSeconds = 0.12;

    public override SoundId Id => SoundId.Splash;

    protected override double DurationSeconds => 2.2;

    protected override float PeakDb => -6f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var noise = new NoiseSource(random.NextUInt());
        var impact = new Biquad(FilterKind.LowPass, 500.0);
        var spray = new Biquad(FilterKind.BandPass, 2200.0, 0.5);
        var sprayTop = new Biquad(FilterKind.LowPass, 6000.0);
        var thump = new Oscillator();
        var turbulence = new Flutter(random.NextUInt(), 12.0);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            thump.FrequencyHz = 42.0 + (45.0 * Math.Exp(-t / 0.08));
            var wash = Shapes.AttackDecay(t - 0.02, 0.05, 0.5) * (1f + (0.3f * turbulence.Next()));
            output[i] = (impact.Process(noise.Pink()) * Shapes.AttackDecay(t, 0.006, 0.14))
                + (thump.Sine() * Shapes.AttackDecay(t, 0.004, 0.11) * 0.8f)
                + (sprayTop.Process(spray.Process(noise.White())) * wash * 0.4f);
        }

        for (var bubble = 0; bubble < BubbleCount; bubble++)
        {
            var u = random.NextFloat();
            AddBubble(output, 0.15 + (1.25 * u * u), random.Range(450f, 1200f), random.Range(0.06f, 0.14f));
        }

        new Biquad(FilterKind.HighPass, 30.0).Process(output);
    }

    private static void AddBubble(Span<float> output, double startSeconds, double baseHz, float amplitude)
    {
        var start = AudioFormat.ToSamples(startSeconds);
        var length = Math.Min(AudioFormat.ToSamples(BubbleSeconds), output.Length - start);
        var phase = 0.0;
        for (var i = 0; i < length; i++)
        {
            var t = i * AudioFormat.SamplePeriodSeconds;
            phase += baseHz * (1.0 + (0.35 * Math.Min(1.0, t / 0.03))) * AudioFormat.SamplePeriodSeconds;
            output[start + i] += (float)Math.Sin(2.0 * Math.PI * phase) * Shapes.AttackDecay(t, 0.002, 0.018) * amplitude;
        }
    }
}
