namespace Usa.Game.Presentation.CrewFeed;

/// <summary>Tuning for <see cref="CrewFeedStateMapper"/>. Thresholds follow GDD 12.5 (I5/I6), 7.3 and MIX.md.</summary>
/// <param name="SpinEnterDps">Rate at which the feed switches to spin strain.</param>
/// <param name="SpinExitDps">Rate below which it leaves spin strain (hysteresis).</param>
/// <param name="SpinFullDps">Rate of full spin intensity (I6, 250 °/s).</param>
/// <param name="SpinStrainedFaceDps">From this rate the face is strained rather than concerned (I5, 120 °/s).</param>
/// <param name="GLoadEnter">Sensed g at which the feed switches to the g-load pose.</param>
/// <param name="GLoadFull">g of full g intensity (sustained ~9 g harms the crew, GDD 7.3).</param>
/// <param name="IntensityTimeConstantSeconds">Smoothing of the spin and g blend parameters.</param>
/// <param name="ExpressionTimeConstantSeconds">Cross-fade time constant between expression presets.</param>
/// <param name="AlarmFaceSeconds">How long the alarmed face holds after an alarm.</param>
/// <param name="ReliefFaceSeconds">How long the relieved face holds after relief.</param>
/// <param name="BlinkMinIntervalSeconds">Shortest gap between blink starts (MODELING 4.4: 3-6 s).</param>
/// <param name="ImpairedBlinkWeight">Blink key held while impaired (eyes closed).</param>
/// <param name="LipGateDbfs">Voice level below which the mouth closes (MIX.md speech sits well above it).</param>
/// <param name="LipFullDbfs">Voice level of a fully open mouth.</param>
/// <param name="LipMinOpen">Opening once the gate is passed; the mouth interior appears from ~0.3 (G2-03).</param>
/// <param name="LipMaxOShare">Largest share of the opening given to <c>mouth_o</c>, drawn per syllable.</param>
/// <param name="MaxStepSeconds">Longest time step applied in one update, so a hitch never jumps the face.</param>
public sealed record CrewFeedSettings(
    double SpinEnterDps = 60.0,
    double SpinExitDps = 50.0,
    double SpinFullDps = 250.0,
    double SpinStrainedFaceDps = 120.0,
    double GLoadEnter = 2.0,
    double GLoadExit = 1.7,
    double GLoadFull = 9.0,
    double IntensityTimeConstantSeconds = 0.4,
    double ExpressionTimeConstantSeconds = 0.35,
    double AlarmFaceSeconds = 3.0,
    double ReliefFaceSeconds = 4.0,
    double BlinkMinIntervalSeconds = 3.0,
    double BlinkMaxIntervalSeconds = 6.0,
    double BlinkCloseSeconds = 0.06,
    double BlinkClosedSeconds = 0.04,
    double BlinkOpenSeconds = 0.10,
    float ImpairedBlinkWeight = 1.0f,
    double BlinkHoldTimeConstantSeconds = 0.15,
    double LipGateDbfs = -30.0,
    double LipFullDbfs = -10.0,
    double LipAttackSeconds = 0.03,
    double LipReleaseSeconds = 0.09,
    float LipMinOpen = 0.3f,
    float LipMaxOShare = 0.6f,
    double MaxStepSeconds = 0.25)
{
    public static CrewFeedSettings Default { get; } = new();

    public double BlinkDurationSeconds => BlinkCloseSeconds + BlinkClosedSeconds + BlinkOpenSeconds;
}
