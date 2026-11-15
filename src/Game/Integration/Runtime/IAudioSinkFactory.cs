using Usa.Game.Presentation.Cues;

namespace Usa.Game.Integration.Runtime;

/// <summary>Supplies the audio cue mapper's sink (the game's <c>AudioDirectorSink</c>; tests record calls).</summary>
public interface IAudioSinkFactory
{
    IAudioSink CreateSink();
}
