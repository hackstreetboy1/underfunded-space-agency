namespace Usa.Game.Presentation.Audio.Mixing;

/// <param name="LevelDb">Mix level of the bus at 100 % player volume.</param>
/// <param name="Voices">Voice pool size (concurrent sounds) of the bus.</param>
public sealed record BusMix(AudioBus Bus, float LevelDb, int Voices);

/// <param name="DepthDb">Ambience attenuation while the Speech bus is active.</param>
/// <param name="KeyThresholdDb">Speech-bus peak above which ducking engages.</param>
public sealed record DuckingSettings(
    float DepthDb,
    float KeyThresholdDb,
    double AttackSeconds,
    double HoldSeconds,
    double ReleaseSeconds);

/// <param name="ReminderAttenuationDb">How far the post-acknowledgement reminder sits below the master alarm (authored into the sound).</param>
public sealed record AlarmCadence(
    double WarningRepeatSeconds,
    double ReminderIntervalSeconds,
    float ReminderAttenuationDb);

/// <summary>Radio colour on the Speech bus: a light band-pass and gentle compression.</summary>
public sealed record RadioVoiceSettings(
    float HighPassHz,
    float LowPassHz,
    float CompressorThresholdDb,
    float CompressorRatio,
    float CompressorAttackMs,
    float CompressorReleaseMs,
    float CompressorMakeupDb);

public sealed record MixProfile(
    IReadOnlyList<BusMix> Buses,
    DuckingSettings Ducking,
    AlarmCadence Alarms,
    RadioVoiceSettings RadioVoice,
    float MasterCeilingDb)
{
    /// <summary>The mix signed off in docs/review/gate-7/MIX.md.</summary>
    public static MixProfile Default { get; } = new(
        [
            new BusMix(AudioBus.Speech, 0f, 3),
            // +4 dB = the -2 dB signed off at Gate 7 plus 6 dB that CueTable.AlarmVoiceGainDb takes off every alarm voice.
            new BusMix(AudioBus.Alarms, 4f, 3),
            // +6 dB = the -4 dB signed off at Gate 7 plus 10 dB that CueTable.SfxVoiceGainDb takes off every SFX voice.
            new BusMix(AudioBus.Sfx, 6f, 8),
            new BusMix(AudioBus.Ambience, -6f, 2),
            new BusMix(AudioBus.Ui, -4f, 4),
        ],
        new DuckingSettings(DepthDb: 9f, KeyThresholdDb: -40f, AttackSeconds: 0.08, HoldSeconds: 0.3, ReleaseSeconds: 0.6),
        new AlarmCadence(WarningRepeatSeconds: 10.0, ReminderIntervalSeconds: 30.0, ReminderAttenuationDb: 18f),
        new RadioVoiceSettings(
            HighPassHz: 250f,
            LowPassHz: 4000f,
            CompressorThresholdDb: -18f,
            CompressorRatio: 3f,
            CompressorAttackMs: 5f,
            CompressorReleaseMs: 120f,
            CompressorMakeupDb: 2f),
        MasterCeilingDb: -1f);

    public BusMix Bus(AudioBus bus)
    {
        foreach (var mix in Buses)
        {
            if (mix.Bus == bus) return mix;
        }

        throw new ArgumentOutOfRangeException(nameof(bus), bus, "Bus missing from the mix profile.");
    }
}
