using Usa.Game.Presentation.Audio;
using Usa.Game.Presentation.Cues;
using Usa.Game.Presentation.Cues.Hosting;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary>Audio cues go to the app's <see cref="AudioDirector"/>.</summary>
public sealed class AudioDirectorSinkFactory(AudioDirector director) : IAudioSinkFactory
{
    private readonly AudioDirector _director = director ?? throw new ArgumentNullException(nameof(director));

    public IAudioSink CreateSink() => new AudioDirectorSink(_director);
}
