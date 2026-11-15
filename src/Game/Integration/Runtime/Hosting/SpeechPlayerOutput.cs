using Godot;
using Usa.Game.Services.Voice;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary><see cref="ICrewSpeechOutput"/> over the app's <see cref="SpeechPlayer"/>.</summary>
public sealed class SpeechPlayerOutput(SpeechPlayer speech) : ICrewSpeechOutput
{
    private readonly SpeechPlayer _speech = speech ?? throw new ArgumentNullException(nameof(speech));

    public void Silence()
    {
        if (!GodotObject.IsInstanceValid(_speech)) return;
        _speech.StopAll();
        _speech.SetPaused(false);
    }
}
