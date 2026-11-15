using Usa.Game.Presentation.Audio;
using Usa.Game.Presentation.Audio.Mixing;

namespace Usa.Game.Presentation.Cues.Hosting;

/// <summary>
/// Game-side <see cref="IAudioSink"/>: forwards <see cref="AudioCueMapper"/> commands to the <see cref="AudioDirector"/>.
/// Wiring: <c>marshaller.Register&lt;ISimEvent&gt;(mapper.Handle)</c> once per session, <c>mapper.Sample(...)</c> after
/// each event flush, <c>mapper.Reset()</c> when the session closes.
/// </summary>
public sealed class AudioDirectorSink(AudioDirector director) : IAudioSink
{
    private readonly AudioDirector _director = director ?? throw new ArgumentNullException(nameof(director));

    public void Play(AudioCue cue, float intensity) => _director.Play(cue, intensity);

    public void StartLoop(AudioCue cue, float intensity) => _director.StartLoop(cue, intensity);

    public void SetLoopIntensity(AudioCue cue, float intensity) => _director.SetLoopIntensity(cue, intensity);

    public void StopLoop(AudioCue cue) => _director.StopLoop(cue);

    public void RaiseAlarm(string alarmId, AlarmPriority priority) => _director.RaiseAlarm(alarmId, priority);

    public void ClearAlarm(string alarmId) => _director.ClearAlarm(alarmId);

    public void AcknowledgeAlarms() => _director.AcknowledgeAlarms();
}
