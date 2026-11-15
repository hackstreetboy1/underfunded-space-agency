namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>Fixed format of every procedural sound: mono float samples at 48 kHz.</summary>
public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const float SampleRateF = SampleRate;
    public const double SamplePeriodSeconds = 1.0 / SampleRate;

    public static int ToSamples(double seconds) => (int)Math.Round(seconds * SampleRate);
}
