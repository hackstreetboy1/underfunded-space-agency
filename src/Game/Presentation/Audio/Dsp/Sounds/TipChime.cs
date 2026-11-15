namespace Usa.Game.Presentation.Audio.Dsp.Sounds;

/// <summary>Gentle ascending bell (C6 then G6) with inharmonic partials; quieter and higher than the caution chime.</summary>
public sealed class TipChime(uint seed) : OneShotSound(seed)
{
    private const double SecondNoteSeconds = 0.09;

    public override SoundId Id => SoundId.TipChime;

    protected override double DurationSeconds => 1.1;

    protected override float PeakDb => -18f;

    protected override double FadeOutSeconds => 0.1;

    protected override void Synthesize(Span<float> output, SynthRandom random)
    {
        var first = Bell(1046.5);
        var second = Bell(1567.98);
        for (var i = 0; i < output.Length; i++)
        {
            var t = TimeAt(i);
            output[i] = (Ring(first) * Shapes.AttackDecay(t, 0.005, 0.3))
                + (Ring(second) * Shapes.AttackDecay(t - SecondNoteSeconds, 0.005, 0.3) * 0.7f);
        }
    }

    private static Oscillator[] Bell(double fundamentalHz) =>
        [new(fundamentalHz), new(fundamentalHz * 2.76), new(fundamentalHz * 5.4)];

    private static float Ring(Oscillator[] partials) =>
        partials[0].Sine() + (0.22f * partials[1].Sine()) + (0.06f * partials[2].Sine());
}
