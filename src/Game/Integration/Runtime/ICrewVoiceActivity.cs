using Usa.Simulation.Kernel;

namespace Usa.Game.Integration.Runtime;

/// <summary>Who is talking on the loop this frame, for the crew feeds' talk and listen states (speech playback, push-to-talk).</summary>
public interface ICrewVoiceActivity
{
    CrewVoiceState For(CrewId crew);
}

/// <param name="Speaking">The crew member's voice line is playing.</param>
/// <param name="LevelDbfs">That line's output level; ignored when not speaking.</param>
/// <param name="Listening">The ground is transmitting to this crew member.</param>
public readonly record struct CrewVoiceState(bool Speaking, double LevelDbfs, bool Listening)
{
    public static CrewVoiceState Silent { get; } = new(false, double.NegativeInfinity, false);
}
