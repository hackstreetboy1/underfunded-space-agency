namespace Usa.Game.Integration.Runtime;

/// <summary>The app-wide crew speech output a run plays through (the game's <c>SpeechPlayer</c>).</summary>
public interface ICrewSpeechOutput
{
    /// <summary>
    /// The run is over: drops every line queued or playing (no caption or voice of it reaches the menu or the next run) and
    /// releases a pause a comms outage left on.
    /// </summary>
    void Silence();
}
