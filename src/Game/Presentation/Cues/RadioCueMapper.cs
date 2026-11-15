using Usa.Game.Presentation.Audio.Mixing;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Cues;

/// <summary>
/// Radio sounds around speech (GDD 14, gate-7 REVIEW.md): the PTT open/close chirps and the static bed under crew
/// transmissions. The comms host reports the player's push-to-talk key and each crew line's start and end; the mapper
/// turns them into <see cref="AudioCue.PttOpen"/>, <see cref="AudioCue.PttClose"/> and the <see cref="AudioCue.RadioStatic"/>
/// loop on the Speech bus.
/// </summary>
/// <remarks>
/// <para>A crew transmission opens with the first line that starts (key-up chirp, static bed) and closes
/// <see cref="RadioCueSettings.HangSeconds"/> after the last one ends (static off, key-down chirp). Lines from several
/// speakers, or a queue of lines from one, share a transmission. A line that was cut (run stopped) closes it at once without
/// a chirp. The player's own key chirps on press and release; it gets no static bed (you do not hear your own carrier).</para>
/// <para>While speech is held (<see cref="SetHeld"/>: the app freeze or a comms outage) no chirp sounds and a pending close
/// waits; the director's freeze already pauses the static bed with the voice. Knowledge: the mapper hears only what the
/// player hears (the key and the lines), and the bed's level is constant.</para>
/// </remarks>
public sealed class RadioCueMapper
{
    private readonly IAudioSink _sink;
    private readonly IMonotonicClock _clock;
    private readonly RadioCueSettings _settings;
    private readonly HashSet<object> _openLines = new(ReferenceEqualityComparer.Instance);
    private double? _closeAtSeconds;
    private bool _held;

    public RadioCueMapper(IAudioSink sink, IMonotonicClock clock, RadioCueSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(clock);
        _sink = sink;
        _clock = clock;
        _settings = settings ?? RadioCueSettings.Default;
    }

    /// <summary>A crew transmission is keyed (static bed running), including its hang after the last line.</summary>
    public bool TransmissionOpen { get; private set; }

    /// <summary>The player's push-to-talk is held (the comms panel shows "Listening").</summary>
    public bool PlayerKeyed { get; private set; }

    private double NowSeconds => _clock.Elapsed.TotalSeconds;

    /// <summary>Speech is held by the app freeze or a comms outage; call every frame and before forwarding an event.</summary>
    public void SetHeld(bool held)
    {
        if (_held == held) return;
        _held = held;
        if (!held && _closeAtSeconds is not null) _closeAtSeconds = NowSeconds + _settings.HangSeconds;
    }

    /// <summary>The player's push-to-talk key went down (true) or up (false), whatever ended the take.</summary>
    public void SetPlayerKeyed(bool keyed)
    {
        if (PlayerKeyed == keyed) return;
        PlayerKeyed = keyed;
        Chirp(keyed ? AudioCue.PttOpen : AudioCue.PttClose);
    }

    /// <summary>A crew line started playing (or, caption-only, started showing). Lines are matched by reference.</summary>
    public void LineStarted(object line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!_openLines.Add(line)) return;
        _closeAtSeconds = null;
        if (TransmissionOpen) return;
        TransmissionOpen = true;
        Chirp(AudioCue.PttOpen);
        _sink.StartLoop(AudioCue.RadioStatic, Math.Clamp(_settings.StaticIntensity, 0f, 1f));
    }

    /// <summary>A line ended; unknown lines (never started here) are ignored.</summary>
    /// <param name="cut">Stopped rather than finished (run stopped): when it was the last line, the static stops at once
    /// and no key-down chirp sounds.</param>
    public void LineEnded(object line, bool cut)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!_openLines.Remove(line) || _openLines.Count > 0) return;
        if (cut) Close(chirp: false);
        else _closeAtSeconds = NowSeconds + _settings.HangSeconds;
    }

    /// <summary>Call once per frame: closes a transmission whose hang has run out.</summary>
    public void Update()
    {
        if (_held || _closeAtSeconds is not { } closeAt || NowSeconds < closeAt) return;
        Close(chirp: true);
    }

    /// <summary>Silently stops the static bed and forgets every line and the key (run stopped, controller freed).</summary>
    public void Reset()
    {
        if (TransmissionOpen) _sink.StopLoop(AudioCue.RadioStatic);
        TransmissionOpen = false;
        PlayerKeyed = false;
        _openLines.Clear();
        _closeAtSeconds = null;
    }

    private void Close(bool chirp)
    {
        _closeAtSeconds = null;
        if (!TransmissionOpen) return;
        TransmissionOpen = false;
        _sink.StopLoop(AudioCue.RadioStatic);
        if (chirp) Chirp(AudioCue.PttClose);
    }

    private void Chirp(AudioCue cue)
    {
        if (!_held) _sink.Play(cue, Math.Clamp(_settings.ChirpIntensity, 0f, 1f));
    }
}
