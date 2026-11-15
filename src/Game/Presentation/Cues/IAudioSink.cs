using Usa.Game.Presentation.Audio.Mixing;

namespace Usa.Game.Presentation.Cues;

/// <summary>
/// The audio commands <see cref="AudioCueMapper"/> issues. The game implements it over
/// <c>AudioDirector</c> (<c>Cues/Hosting/AudioDirectorSink</c>); tests record the calls.
/// </summary>
public interface IAudioSink
{
    /// <summary>A one-shot cue; intensity 0..1 scales its level.</summary>
    void Play(AudioCue cue, float intensity);

    void StartLoop(AudioCue cue, float intensity);

    void SetLoopIntensity(AudioCue cue, float intensity);

    void StopLoop(AudioCue cue);

    void RaiseAlarm(string alarmId, AlarmPriority priority);

    void ClearAlarm(string alarmId);

    void AcknowledgeAlarms();
}
