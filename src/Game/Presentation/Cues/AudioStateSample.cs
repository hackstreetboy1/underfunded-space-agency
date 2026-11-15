namespace Usa.Game.Presentation.Cues;

/// <summary>
/// Continuous state the host samples once per frame for <see cref="AudioCueMapper.Sample"/>. Like every channel besides the
/// readouts it may carry only what the player can see or already knows (GDD 4.2, 4.4): hidden faults never reach it.
/// </summary>
/// <param name="EntryHeatingIntensity">Entry heating normalised to 0..1 (e.g. the flight heating proxy over its nominal peak).</param>
/// <param name="BurnActive">A powered segment is running (also learned from <c>BurnStarted</c>/<c>BurnEnded</c>).</param>
/// <param name="Docked">The stack is latched; more thrusters can pulse, so the pulse cap is higher.</param>
/// <param name="AttitudePulses">Attitude-thruster firings that began since the previous sample, counting only thrusters whose
/// plume the external view may show.</param>
public readonly record struct AudioStateSample(
    double EntryHeatingIntensity,
    bool BurnActive,
    bool Docked,
    int AttitudePulses = 0);
