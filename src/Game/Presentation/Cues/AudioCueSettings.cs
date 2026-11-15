namespace Usa.Game.Presentation.Cues;

/// <summary>Tuning for <see cref="AudioCueMapper"/>. These are presentation choices, not simulation rules.</summary>
/// <param name="PulsesPerSecond">Sustained cap on thruster-pulse one-shots while undocked.</param>
/// <param name="DockedPulsesPerSecond">Cap while docked (two vehicles' thrusters can fire).</param>
/// <param name="PulseBurst">Pulses allowed back to back before the cap applies.</param>
/// <param name="EntryRoarOnIntensity">Heating at which the roar starts without an interface crossing.</param>
/// <param name="EntryRoarOffIntensity">Heating below which the roar stops once outside the atmosphere (hysteresis).</param>
/// <param name="EntryRoarFloorIntensity">Roar level between the interface crossing and measurable heating.</param>
/// <param name="LoopIntensityEpsilon">Smaller loop-intensity changes are not sent (the generators smooth anyway).</param>
/// <param name="BrownoutAmbienceIntensity">Cabin bed level while any bus is browned out.</param>
/// <param name="AdvisoryMinIntervalSeconds">Repeat advisories for the same id closer than this are dropped.</param>
/// <param name="OneShotMinIntervalSeconds">Repeats of the same event-driven one-shot closer than this are dropped.</param>
public sealed record AudioCueSettings(
    double PulsesPerSecond = 6.0,
    double DockedPulsesPerSecond = 9.0,
    int PulseBurst = 3,
    float PulseIntensity = 0.8f,
    float EntryRoarOnIntensity = 0.03f,
    float EntryRoarOffIntensity = 0.01f,
    float EntryRoarFloorIntensity = 0.05f,
    float RetroBurnIntensity = 1.0f,
    float PoweredBurnIntensity = 0.55f,
    float DrogueIntensity = 0.8f,
    float MainChuteIntensity = 1.0f,
    float FailedChuteIntensity = 0.7f,
    float JettisonIntensity = 0.6f,
    double SplashSpeedForFullIntensityMps = 12.0,
    float SplashMinIntensity = 0.5f,
    float LoopIntensityEpsilon = 0.02f,
    bool ManageCabinAmbience = true,
    float CabinAmbienceIntensity = 1.0f,
    float BrownoutAmbienceIntensity = 0.55f,
    double AdvisoryMinIntervalSeconds = 5.0,
    double OneShotMinIntervalSeconds = 0.5)
{
    public static AudioCueSettings Default { get; } = new();
}
