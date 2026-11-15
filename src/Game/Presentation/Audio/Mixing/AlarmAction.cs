namespace Usa.Game.Presentation.Audio.Mixing;

public enum AlarmActionKind
{
    PlayOnce,
    StartLoop,
    StopLoop,
}

public readonly record struct AlarmAction(AlarmActionKind Kind, AudioCue Cue);
