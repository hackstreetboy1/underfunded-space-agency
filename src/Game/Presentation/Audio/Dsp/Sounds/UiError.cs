namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>Two soft, low, low-passed square buzzes, the second a step lower.</summary>
public sealed class UiError(uint seed) : OneShotSound(seed)
{
    public override SoundId Id => SoundId.UiError;

    protected override double DurationSeconds => 0.3;

    protected override float PeakDb => -16f;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var buzz = new Oscillator(233.08);
        var soften = new Biquad(FilterKind.LowPass, 1400.0);
        var envelope = new Adsr(0.004, 0.05, 0.6f, 0.03);
        var buzzLength = AudioFormat.ToSamples(0.07);
        var secondStart = AudioFormat.ToSamples(0.12);
        for (var i = 0; i < output.Length; i++)
        {
            if (i == 0 || i == secondStart) envelope.GateOn();
            if (i == buzzLength || i == secondStart + buzzLength) envelope.GateOff();
            if (i == secondStart) buzz.FrequencyHz = 196.0;
            output[i] = soften.Process(buzz.Square()) * envelope.Next();
        }
    }
}
