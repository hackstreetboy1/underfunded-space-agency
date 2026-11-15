namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>Soft single hi/lo pair of the master alarm, 18 dB down, as the post-acknowledgement reminder.</summary>
public sealed class AlarmReminder(uint seed) : OneShotSound(seed)
{
    private static readonly float[] Partials = [0.1f];

    public override SoundId Id => SoundId.AlarmReminder;

    protected override double DurationSeconds => 0.45;

    protected override float PeakDb => -21f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var high = new Oscillator(AlarmMaster.HighHz);
        var low = new Oscillator(AlarmMaster.LowHz);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            output[i] = (high.Additive(Partials) * Shapes.Gate(t, 0.0, 0.15, 0.012))
                + (low.Additive(Partials) * Shapes.Gate(t, 0.2, 0.15, 0.012));
        }
    }
}
