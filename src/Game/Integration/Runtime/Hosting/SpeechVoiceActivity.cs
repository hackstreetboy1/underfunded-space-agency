using Usa.Game.Services.Voice;
using Usa.Simulation.Kernel;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary>
/// <see cref="ICrewVoiceActivity"/> over the app's <see cref="SpeechPlayer"/> and <see cref="MicCapture"/>: a crew member speaks
/// while their line plays (not while speech is held by an outage or the freeze), at the speech bus's analysed level for
/// lip-flap; every crew member listens while push-to-talk is held. Polled each frame; it subscribes to nothing.
/// </summary>
public sealed class SpeechVoiceActivity(SpeechPlayer speech, MicCapture mic) : ICrewVoiceActivity
{
    private readonly SpeechPlayer _speech = speech ?? throw new ArgumentNullException(nameof(speech));
    private readonly MicCapture _mic = mic ?? throw new ArgumentNullException(nameof(mic));

    public CrewVoiceState For(CrewId crew)
    {
        var listening = _mic.IsCapturing;
        if (_speech.Paused || !_speech.IsSpeaking(crew.Value)) return CrewVoiceState.Silent with { Listening = listening };
        return new CrewVoiceState(true, MeterToDbfs(_speech.GetOutputLevel(crew.Value)), listening);
    }

    /// <summary>Inverse of <see cref="PcmLevel.ToMeter"/>: 0 is silence, 1 is 0 dBFS.</summary>
    public static double MeterToDbfs(float meter) =>
        meter <= 0f || float.IsNaN(meter) ? double.NegativeInfinity : Math.Min(1f, meter) * -PcmLevel.FloorDb + PcmLevel.FloorDb;
}
