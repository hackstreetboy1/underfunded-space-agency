namespace Usa.Game.Presentation.Cues;

/// <summary>Tuning for <see cref="RadioCueMapper"/>. Presentation choices only; none of them may follow simulation state.</summary>
/// <param name="HangSeconds">A crew transmission stays keyed this long after its last line ends, so back-to-back lines are one
/// transmission (one key-up, one key-down) instead of a chirp pair between every line.</param>
/// <param name="StaticIntensity">Fixed level of the static bed. Constant on purpose: a bed that changed with the spacecraft's
/// condition would report faults the player has not learned (GDD 4.2).</param>
/// <param name="ChirpIntensity">Level of the PTT open/close chirps.</param>
public sealed record RadioCueSettings(
    double HangSeconds = 0.25,
    float StaticIntensity = 1.0f,
    float ChirpIntensity = 1.0f)
{
    public static RadioCueSettings Default { get; } = new();
}
