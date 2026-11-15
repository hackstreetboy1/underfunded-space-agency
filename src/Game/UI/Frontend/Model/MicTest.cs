namespace Usa.Game.UI.Frontend.Model;

public enum MicTestState
{
    Idle,
    Listening,

    /// <summary>The meter rose above <see cref="MicTest.HeardLevel"/> during the take.</summary>
    Heard,

    /// <summary>The take ran its length and the meter never rose.</summary>
    Silent,

    /// <summary>The microphone is off, or capture was refused (frozen, already recording).</summary>
    Unavailable,
}

/// <summary>
/// The Settings "Test microphone" take (Voice &amp; privacy): about <see cref="DurationS"/> of level metering through
/// <c>MicCapture</c>, then a verdict. The take is always cancelled, never released, so nothing is encoded or transcribed.
/// </summary>
public sealed class MicTest
{
    public const double DurationS = 3.0;

    /// <summary>Meter reading (0..1, dB scale) that counts as hearing the player; quiet-room noise stays below it.</summary>
    public const float HeardLevel = 0.15f;

    public MicTestState State { get; private set; } = MicTestState.Idle;

    public float PeakLevel { get; private set; }

    public double ElapsedS { get; private set; }

    /// <summary>0..1 through the take.</summary>
    public double Progress => Math.Clamp(ElapsedS / DurationS, 0, 1);

    /// <param name="started">Whether <c>MicCapture.BeginCapture</c> accepted the take.</param>
    public void Begin(bool started)
    {
        ElapsedS = 0;
        PeakLevel = 0;
        State = started ? MicTestState.Listening : MicTestState.Unavailable;
    }

    /// <summary>Feeds one frame's meter reading.</summary>
    /// <returns>True when the take just ended: cancel the capture now.</returns>
    public bool Advance(double deltaS, float level)
    {
        if (State != MicTestState.Listening) return false;
        if (double.IsFinite(deltaS) && deltaS > 0) ElapsedS += deltaS;
        if (float.IsFinite(level)) PeakLevel = Math.Max(PeakLevel, Math.Clamp(level, 0f, 1f));
        if (ElapsedS < DurationS) return false;
        State = PeakLevel >= HeardLevel ? MicTestState.Heard : MicTestState.Silent;
        return true;
    }

    /// <summary>The take was stopped from outside (screen closed, mic turned off); no verdict.</summary>
    public void Abort()
    {
        if (State == MicTestState.Listening) State = MicTestState.Idle;
    }

    /// <summary>Peak as a whole percentage for the verdict line.</summary>
    public int PeakPercent => (int)Math.Round(PeakLevel * 100, MidpointRounding.AwayFromZero);
}
