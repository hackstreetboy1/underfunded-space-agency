namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>One warning burst: five 880 Hz pulses at 4 per second. The director repeats it.</summary>
public sealed class AlarmWarning(uint seed) : OneShotSound(seed)
{
    public const double PulsePeriodSeconds = 0.25;
    public const double PulseLengthSeconds = 0.13;
    public const int PulseCount = 5;
    public const double ToneHz = 880.0;
    private static readonly float[] Partials = [0f, 0.3f, 0f, 0.12f];

    public override SoundId Id => SoundId.AlarmWarning;

    protected override double DurationSeconds => PulseCount * PulsePeriodSeconds;

    protected override float PeakDb => -5f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var tone = new Oscillator(ToneHz);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            var pulseStart = Math.Floor(t / PulsePeriodSeconds) * PulsePeriodSeconds;
            output[i] = tone.Additive(Partials) * Shapes.Gate(t, pulseStart, PulseLengthSeconds, 0.008);
        }
    }
}
