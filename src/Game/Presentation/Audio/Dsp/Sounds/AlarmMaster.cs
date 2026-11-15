namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>
/// Master alarm until acknowledged: hi/lo tones (1200/800 Hz) at 5 tones per second, four tones
/// then a 0.23 s breath, so the pattern repeats every second.
/// </summary>
public sealed class AlarmMaster(float[] table, float intensity = 1f)
    : LoopSound(table, intensity, fadeInSeconds: 0.0, fadeOutSeconds: 0.12)
{
    public const double HighHz = 1200.0;
    public const double LowHz = 800.0;
    public const double ToneSpacingSeconds = 0.2;
    public const double ToneLengthSeconds = 0.17;
    public const double CycleSeconds = 1.0;
    private static readonly float[] Partials = [0.2f, 0.35f, 0f, 0.1f];

    public override SoundId Id => SoundId.AlarmMaster;

    public static float[] BuildTable()
    {
        var table = new float[TableLength(CycleSeconds)];
        var high = new Oscillator(HighHz);
        var low = new Oscillator(LowHz);
        for (var i = 0; i < table.Length; i++)
        {
            var t = i * AudioFormat.SamplePeriodSeconds;
            var highSample = high.Additive(Partials);
            var lowSample = low.Additive(Partials);
            for (var tone = 0; tone < 4; tone++)
            {
                var gate = Shapes.Gate(t, tone * ToneSpacingSeconds, ToneLengthSeconds, 0.006);
                table[i] += (tone % 2 == 0 ? highSample : lowSample) * gate;
            }
        }

        Signal.NormalizePeak(table, -3f);
        return table;
    }
}
