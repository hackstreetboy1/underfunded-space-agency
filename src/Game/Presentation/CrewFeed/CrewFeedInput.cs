using Usa.Simulation.Crew;

namespace Usa.Game.Presentation.CrewFeed;

/// <summary>One crew member's presentation inputs for a frame.</summary>
/// <param name="Speaking">The crew member's voice line is playing.</param>
/// <param name="SpeechLevelDbfs">Output level of that voice (peak or short RMS), dBFS; ignored when not speaking.</param>
/// <param name="Listening">The player (or ground) is transmitting to this crew member.</param>
/// <param name="AngularRateDegPerSec">Total body rate the crew member is exposed to.</param>
/// <param name="GLoad">Sensed acceleration in g (1 at rest on the ground, ~0 in orbit).</param>
/// <param name="RecentEvent">Something that happened since the previous update; same as calling <see cref="CrewFeedStateMapper.Notify"/>.</param>
public readonly record struct CrewFeedInput(
    bool Alive,
    HealthBand Health,
    bool Speaking,
    double SpeechLevelDbfs,
    bool Listening,
    CrewActivity Activity,
    double AngularRateDegPerSec,
    double GLoad,
    CrewFeedEvent RecentEvent = CrewFeedEvent.None)
{
    public static CrewFeedInput Calm { get; } = new(true, HealthBand.Stable, false, double.NegativeInfinity, false, CrewActivity.None, 0, 0);
}
