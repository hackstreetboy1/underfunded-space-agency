using Usa.Game.Presentation.Audio.Mixing;
using Usa.Simulation.Session;

namespace Usa.Game.Presentation.Audio;

/// <summary>Maps simulation alarm levels to audio priorities by name (review X6: the enums' numbers differ).</summary>
public static class AlarmLevelMapping
{
    public static AlarmPriority ToAudioPriority(AlarmLevel level) => level switch
    {
        AlarmLevel.Caution => AlarmPriority.Caution,
        AlarmLevel.Warning => AlarmPriority.Warning,
        AlarmLevel.Master => AlarmPriority.Master,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown alarm level."),
    };
}
