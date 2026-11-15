namespace Usa.Game.Presentation.Audio.Mixing;

/// <summary>Game-facing audio cues. Alarm cues are normally driven through the alarm sequencer.</summary>
public enum AudioCue
{
    PttOpen,
    PttClose,
    RadioStatic,
    CabinAmbience,
    ThrusterPulse,
    StuckThruster,
    RetroBurn,
    EntryRoar,
    ChuteDeploy,
    Splashdown,
    UiTick,
    UiConfirm,
    UiError,
    TipShown,
    AlarmAdvisory,
    AlarmCaution,
    AlarmWarning,
    AlarmMaster,
    AlarmReminder,
}
