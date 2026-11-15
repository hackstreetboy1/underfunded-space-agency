using Godot;
using Usa.Game.Presentation.Audio.Mixing;

namespace Usa.Game.Presentation.Audio;

/// <summary>
/// Creates the Speech, Alarms, SFX, Ambience and UI buses under Master in code when the project
/// layout lacks them, so the game never depends on an edited bus-layout resource (MIX.md).
/// </summary>
public static class AudioBusSetup
{
    /// <returns>The gain stage on the Ambience bus that ducking drives.</returns>
    public static AudioEffectAmplify EnsureBuses(MixProfile mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        foreach (var bus in AudioBuses.All)
        {
            var name = AudioBuses.Name(bus);
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            var index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, AudioBuses.MasterName);
            if (bus == AudioBus.Speech) AddRadioVoice(index, mix.RadioVoice);
        }

        var master = AudioServer.GetBusIndex(AudioBuses.MasterName);
        var limiter = FindOrAddEffect<AudioEffectHardLimiter>(master);
        limiter.CeilingDb = mix.MasterCeilingDb;
        return FindOrAddEffect<AudioEffectAmplify>(IndexOf(AudioBus.Ambience));
    }

    public static int IndexOf(AudioBus bus) => AudioServer.GetBusIndex(AudioBuses.Name(bus));

    private static void AddRadioVoice(int bus, RadioVoiceSettings radio)
    {
        AudioServer.AddBusEffect(bus, new AudioEffectHighPassFilter
        {
            CutoffHz = radio.HighPassHz,
            Db = AudioEffectFilter.FilterDB.Filter12Db,
        });
        AudioServer.AddBusEffect(bus, new AudioEffectLowPassFilter
        {
            CutoffHz = radio.LowPassHz,
            Db = AudioEffectFilter.FilterDB.Filter12Db,
        });
        AudioServer.AddBusEffect(bus, new AudioEffectCompressor
        {
            Threshold = radio.CompressorThresholdDb,
            Ratio = radio.CompressorRatio,
            AttackUs = radio.CompressorAttackMs * 1000f,
            ReleaseMs = radio.CompressorReleaseMs,
            Gain = radio.CompressorMakeupDb,
        });
    }

    private static T FindOrAddEffect<T>(int bus)
        where T : AudioEffect, new()
    {
        for (var i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is T existing) return existing;
        }

        var effect = new T();
        AudioServer.AddBusEffect(bus, effect);
        return effect;
    }
}
