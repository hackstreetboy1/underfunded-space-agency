namespace Usa.Game.Presentation.Audio.Mixing;

/// <summary>
/// Alarm priorities of UI.md 3.10. Names match the simulation's <c>AlarmLevel</c> (which has no Advisory), but the
/// numbers do not: never cast between them, map with <c>AlarmLevelMapping.ToAudioPriority</c>.
/// </summary>
public enum AlarmPriority
{
    Advisory = 0,
    Caution = 1,
    Warning = 2,
    Master = 3,
}
