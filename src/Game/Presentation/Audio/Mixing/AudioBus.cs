namespace Usa.Game.Presentation.Audio.Mixing;

/// <summary>Mixer buses under Master (GDD 14). Each has its own player-facing volume.</summary>
public enum AudioBus
{
    Speech,
    Alarms,
    Sfx,
    Ambience,
    Ui,
}

public static class AudioBuses
{
    public const string MasterName = "Master";

    public static IReadOnlyList<AudioBus> All { get; } =
        [AudioBus.Speech, AudioBus.Alarms, AudioBus.Sfx, AudioBus.Ambience, AudioBus.Ui];

    public static string Name(AudioBus bus) => bus switch
    {
        AudioBus.Speech => "Speech",
        AudioBus.Alarms => "Alarms",
        AudioBus.Sfx => "SFX",
        AudioBus.Ambience => "Ambience",
        AudioBus.Ui => "UI",
        _ => throw new ArgumentOutOfRangeException(nameof(bus), bus, null),
    };
}
