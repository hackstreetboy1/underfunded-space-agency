using Godot;
using Usa.Game.Comms;
using Usa.Game.Comms.Hosting;
using Usa.Game.Presentation.Audio;
using Usa.Game.Presentation.Cues;
using Usa.Game.Presentation.Cues.Hosting;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.Services.Ai;
using Usa.Game.Services.Connection;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Voice;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary>
/// Attaches each composed orchestrator to a new <see cref="CommsController"/> under <paramref name="owner"/>, with the radio's
/// push-to-talk chirps and static bed played through the <see cref="AudioDirector"/>.
/// </summary>
public sealed class CommsControllerHostFactory(
    Node owner, AiGateway gateway, ConnectionMonitor connection, MicCapture mic, SpeechPlayer speech, Log log, AudioDirector audio,
    IMonotonicClock clock) : ICommsHostFactory
{
    public ICommsHost Attach(CommsOrchestrator comms)
    {
        ArgumentNullException.ThrowIfNull(comms);
        var controller = new CommsController();
        controller.Initialize(comms, gateway, connection, mic, speech, log);
        controller.AttachRadio(new RadioCueMapper(new AudioDirectorSink(audio), clock));
        owner.AddChild(controller);
        return new CommsControllerHost(controller);
    }
}
