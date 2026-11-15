namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>Linear-attack, exponential decay/release ADSR envelope driven by a gate.</summary>
public sealed class Adsr(double attackSeconds, double decaySeconds, float sustainLevel, double releaseSeconds)
{
    private enum Stage
    {
        Idle,
        Attack,
        Decay,
        Sustain,
        Release,
    }

    private readonly float _attackStep = (float)(1.0 / Math.Max(1.0, attackSeconds * AudioFormat.SampleRate));
    private readonly float _decayCoefficient = Coefficient(decaySeconds);
    private readonly float _releaseCoefficient = Coefficient(releaseSeconds);
    private Stage _stage;

    public float Level { get; private set; }

    public bool IsIdle => _stage == Stage.Idle;

    public void GateOn() => _stage = Stage.Attack;

    public void GateOff()
    {
        if (_stage != Stage.Idle) _stage = Stage.Release;
    }

    public float Next()
    {
        switch (_stage)
        {
            case Stage.Attack:
                Level += _attackStep;
                if (Level >= 1f)
                {
                    Level = 1f;
                    _stage = Stage.Decay;
                }

                break;
            case Stage.Decay:
                Level = sustainLevel + ((Level - sustainLevel) * _decayCoefficient);
                if (Level - sustainLevel < 1e-4f) _stage = Stage.Sustain;
                break;
            case Stage.Sustain:
                Level = sustainLevel;
                break;
            case Stage.Release:
                Level *= _releaseCoefficient;
                if (Level < 1e-5f)
                {
                    Level = 0f;
                    _stage = Stage.Idle;
                }

                break;
            default:
                break;
        }

        return Level;
    }

    // Exponential approach reaching ~-60 dB after the given time.
    private static float Coefficient(double seconds) =>
        (float)Math.Exp(-6.9 / Math.Max(1.0, seconds * AudioFormat.SampleRate));
}
