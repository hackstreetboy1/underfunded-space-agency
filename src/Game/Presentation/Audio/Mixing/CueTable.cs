using Usa.Game.Presentation.Audio.Dsp;

namespace Usa.Game.Presentation.Audio.Mixing;

/// <param name="Priority">0-100; a full bus steals its lowest-priority (then oldest) voice if not higher than this.</param>
/// <param name="GainDb">Voice gain on top of the sound's authored level and the bus level.</param>
/// <param name="MaxInstances">Concurrent instances of this cue; the oldest is released beyond it.</param>
/// <param name="MinIntervalSeconds">Retriggers closer than this are dropped (anti machine-gun).</param>
public sealed record CueSpec(
    AudioCue Cue,
    SoundId Sound,
    AudioBus Bus,
    int Priority,
    float GainDb = 0f,
    int MaxInstances = 1,
    double MinIntervalSeconds = 0.0)
{
    public bool IsLoop => SoundCatalog.Get(Sound).IsLoop;
}

public static class CueTable
{
    /// <summary>
    /// Every Alarms-bus cue is 6 dB down at the voice and the Alarms bus 6 dB up (MixProfile), so what the player hears is
    /// unchanged while the bus keeps headroom before its fader: the three loudest alarms (the bus has three voices)
    /// stacked in phase still peak below -1 dBFS. One shared value keeps the alarms' relative loudness.
    /// </summary>
    public const float AlarmVoiceGainDb = -6f;

    /// <summary>
    /// The same move for the SFX bus: every SFX voice 10 dB down and the bus 10 dB up, so the eight voices busy with the
    /// loudest effects in phase peak below -1 dBFS before the fader (+8.7 dBFS before). Per-cue offsets on top of it keep
    /// the effects' relative loudness.
    /// </summary>
    public const float SfxVoiceGainDb = -10f;

    private const float ThrusterPulseTrimDb = -2f;

    public static IReadOnlyList<CueSpec> All { get; } =
    [
        new(AudioCue.PttOpen, SoundId.PttOpen, AudioBus.Speech, 70, MinIntervalSeconds: 0.1),
        new(AudioCue.PttClose, SoundId.PttClose, AudioBus.Speech, 70, MinIntervalSeconds: 0.1),
        new(AudioCue.RadioStatic, SoundId.RadioStaticBed, AudioBus.Speech, 60),
        new(AudioCue.CabinAmbience, SoundId.CabinHumVentBed, AudioBus.Ambience, 20),
        new(AudioCue.ThrusterPulse, SoundId.ThrusterPulse, AudioBus.Sfx, 30, SfxVoiceGainDb + ThrusterPulseTrimDb, MaxInstances: 3, MinIntervalSeconds: 0.04),
        new(AudioCue.StuckThruster, SoundId.StuckThrusterHiss, AudioBus.Sfx, 50, SfxVoiceGainDb),
        new(AudioCue.RetroBurn, SoundId.RetroRumble, AudioBus.Sfx, 55, SfxVoiceGainDb),
        new(AudioCue.EntryRoar, SoundId.EntryRoar, AudioBus.Sfx, 55, SfxVoiceGainDb),
        new(AudioCue.ChuteDeploy, SoundId.ChuteSnap, AudioBus.Sfx, 60, SfxVoiceGainDb),
        new(AudioCue.Splashdown, SoundId.Splash, AudioBus.Sfx, 60, SfxVoiceGainDb),
        new(AudioCue.UiTick, SoundId.UiTick, AudioBus.Ui, 20, MaxInstances: 2, MinIntervalSeconds: 0.03),
        new(AudioCue.UiConfirm, SoundId.UiConfirm, AudioBus.Ui, 40, MinIntervalSeconds: 0.08),
        new(AudioCue.UiError, SoundId.UiError, AudioBus.Ui, 45, MinIntervalSeconds: 0.15),
        new(AudioCue.TipShown, SoundId.TipChime, AudioBus.Ui, 40, MinIntervalSeconds: 1.0),
        new(AudioCue.AlarmAdvisory, SoundId.UiTick, AudioBus.Alarms, 50, AlarmVoiceGainDb, MinIntervalSeconds: 0.25),
        new(AudioCue.AlarmCaution, SoundId.AlarmCaution, AudioBus.Alarms, 80, AlarmVoiceGainDb, MinIntervalSeconds: 0.5),
        new(AudioCue.AlarmWarning, SoundId.AlarmWarning, AudioBus.Alarms, 90, AlarmVoiceGainDb),
        new(AudioCue.AlarmMaster, SoundId.AlarmMaster, AudioBus.Alarms, 100, AlarmVoiceGainDb),
        new(AudioCue.AlarmReminder, SoundId.AlarmReminder, AudioBus.Alarms, 80, AlarmVoiceGainDb),
    ];

    public static CueSpec Get(AudioCue cue) => All[(int)cue];
}
