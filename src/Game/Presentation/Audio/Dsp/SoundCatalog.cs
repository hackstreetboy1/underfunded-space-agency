namespace Usa.Game.Presentation.Audio.Dsp;

/// <param name="FileName">Snake-case name used for previews and logs.</param>
/// <param name="PreviewIntensity">Intensity used for the review render of a loop.</param>
public sealed record SoundInfo(SoundId Id, string FileName, bool IsLoop, string Intent, float PreviewIntensity = 1f);

public static class SoundCatalog
{
    public static IReadOnlyList<SoundInfo> All { get; } =
    [
        new(SoundId.PttOpen, "ptt_open", false,
            "Radio key-up: tick + 2525 Hz intro tone (Quindar-style) + brief squelch. Marks the start of every transmission."),
        new(SoundId.PttClose, "ptt_close", false,
            "Radio key-down: 2475 Hz outro tone + short band-passed squelch tail. Marks the end of a transmission."),
        new(SoundId.RadioStaticBed, "radio_static_bed", true,
            "Soft band-limited (350-3200 Hz) hiss with slow flutter and sparse crackles under transmissions."),
        new(SoundId.CabinHumVentBed, "cabin_hum_vent_bed", true,
            "Cabin bed: two fans beating at 1.5 Hz, faint 400 Hz inverter whine, low-passed ventilation air."),
        new(SoundId.AlarmCaution, "alarm_caution", false,
            "Caution: descending two-tone chime (G5 -> D5), played once. Noticeable, not urgent."),
        new(SoundId.AlarmWarning, "alarm_warning", false,
            "Warning: five fast 880 Hz pulses (4/s). The director repeats the burst every 10 s until acknowledged."),
        new(SoundId.AlarmMaster, "alarm_master", true,
            "Master alarm: alternating 1200/800 Hz tones at 5 tones/s with a short breath each second, until acknowledged. Loudest sound."),
        new(SoundId.AlarmReminder, "alarm_reminder", false,
            "Attenuated master reminder (one soft hi-lo pair, -18 dB below the master) every 30 s after acknowledgement."),
        new(SoundId.ThrusterPulse, "thruster_pulse", false,
            "External-view RCS pulse: soft pop + short band-passed hiss. Seeded variation per pulse."),
        new(SoundId.StuckThrusterHiss, "stuck_thruster_hiss", true,
            "Continuous high-passed hiss with fluttering pressure and a faint whistle: a thruster that will not close."),
        new(SoundId.RetroRumble, "retro_rumble", true,
            "Retro-rocket burn: low rumble with rough combustion modulation and a 420 Hz body for small speakers."),
        new(SoundId.EntryRoar, "entry_roar", true,
            "Entry plasma/air roar: buffeting filtered noise; intensity opens the low-pass (220 Hz -> 3 kHz) and the level.",
            PreviewIntensity: 0.7f),
        new(SoundId.ChuteSnap, "chute_snap", false,
            "Parachute deploy: mortar crack, low thump, then decaying canopy flutter."),
        new(SoundId.Splash, "splash", false,
            "Splashdown: low impact thump, wash of spray and a few rising bubbles."),
        new(SoundId.UiTick, "ui_tick", false,
            "Quiet short tick for UI interactions and advisories."),
        new(SoundId.UiConfirm, "ui_confirm", false,
            "Two ascending blips (660 -> 990 Hz): an order or setting was accepted."),
        new(SoundId.UiError, "ui_error", false,
            "Two soft low buzzes (233 -> 196 Hz): an action was refused. Not harsh."),
        new(SoundId.TipChime, "tip_chime", false,
            "Gentle ascending bell (C6 -> G6) when a Flight Director tip appears. Quieter and higher than the caution chime."),
    ];

    public static SoundInfo Get(SoundId id) => All[(int)id];
}
