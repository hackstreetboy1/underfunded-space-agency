using System.Globalization;
using System.Runtime.InteropServices;
using Godot;
using Usa.Game.Input;
using Usa.Game.Services.Audio;
using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Voice;

/// <summary>
/// Push-to-talk recorder (SCRIPTING.md 13.3). An <see cref="AudioStreamMicrophone"/> plays into the muted
/// <c>Mic</c> bus whose <see cref="AudioEffectCapture"/> is drained every frame while PTT is held; on release the
/// take is downmixed, resampled to 16 kHz and encoded as PCM16 WAV off the main thread, then
/// <see cref="UtteranceCaptured"/> fires on the main thread. Takes stop at 15 s. Audio never leaves this node:
/// posting to <c>/v1/stt</c> is the comms controller's job.
/// </summary>
public sealed partial class MicCapture : Node
{
    public const int TargetSampleRateHz = 16_000;
    public const double MaxUtteranceSeconds = 15.0;
    public const double MinUtteranceSeconds = 0.3;
    private const string Category = "MicCapture";

    private readonly List<float> _samples = [];
    private AudioStreamPlayer _player = null!;
    private AudioEffectCapture _capture = null!;
    private Log? _log;
    private int _sourceRateHz;
    private int _maxSourceSamples;
    private float _peakMeter;
    private bool _micEnabled = true;
    private bool _listening;
    private bool _frozen;
    private int _takeGeneration;

    /// <summary>When true (default) the node handles the <c>ptt</c> action itself, ignoring it while a text field has focus.</summary>
    [Export]
    public bool HandlePushToTalkInput { get; set; } = true;

    public event Action? CaptureStarted;

    public event Action<CapturedUtterance>? UtteranceCaptured;

    public event Action<CaptureDiscardReason>? CaptureDiscarded;

    public bool IsCapturing { get; private set; }

    public bool MicEnabled => _micEnabled;

    /// <summary>Input level 0..1 (dB scale) of the latest frame while capturing, else 0. For the PTT meter.</summary>
    public float InputLevel { get; private set; }

    /// <summary>Seconds recorded in the current take.</summary>
    public double CapturedSeconds => _sourceRateHz > 0 ? (double)_samples.Count / _sourceRateHz : 0;

    public void Initialize(Log log) => _log = log;

    public override void _Ready()
    {
        var bus = AudioBuses.Ensure(AudioBuses.Mic);
        AudioServer.SetBusMute(bus, true);
        (_capture, _) = AudioBuses.EnsureEffect(bus, () => new AudioEffectCapture { BufferLength = 0.5f });

        _player = new AudioStreamPlayer { Name = "MicStream", Stream = new AudioStreamMicrophone(), Bus = AudioBuses.Mic };
        AddChild(_player);
        _sourceRateHz = (int)AudioServer.GetMixRate();
        _maxSourceSamples = (int)(MaxUtteranceSeconds * _sourceRateHz);
    }

    /// <summary>Mirrors the "microphone on/off" setting. Disabling cancels a take in progress and closes the device.</summary>
    public void SetMicEnabled(bool enabled)
    {
        _micEnabled = enabled;
        if (!enabled)
        {
            if (IsCapturing) CancelCapture();
            _takeGeneration++;
        }
        UpdateStream();
    }

    /// <summary>
    /// Follows the app's one freeze signal (Esc menu, outage, loading): a take in progress is cancelled, one still being
    /// encoded is dropped, and push-to-talk is refused until the freeze ends.
    /// </summary>
    public void SetFrozen(bool frozen)
    {
        if (_frozen == frozen) return;
        _frozen = frozen;
        if (!frozen) return;
        if (IsCapturing) CancelCapture();
        _takeGeneration++;
    }

    /// <summary>
    /// Keeps the input device open between takes (call with true while the mission console is shown) so the
    /// first syllable after pressing PTT isn't lost to device start-up. Nothing is recorded outside a take.
    /// </summary>
    public void SetListening(bool listening)
    {
        _listening = listening;
        UpdateStream();
    }

    public bool BeginCapture()
    {
        if (!_micEnabled || _frozen || IsCapturing || _player is null) return false;

        if (!_player.Playing) _player.Play();
        _capture.ClearBuffer();
        _samples.Clear();
        _peakMeter = 0f;
        InputLevel = 0f;
        IsCapturing = true;
        CaptureStarted?.Invoke();
        return true;
    }

    public void EndCapture() => Finish(truncated: false);

    /// <summary>Discards the take in progress, and any released take still being encoded.</summary>
    public void CancelCapture()
    {
        _takeGeneration++;
        if (!IsCapturing) return;
        StopTake();
        _samples.Clear();
        CaptureDiscarded?.Invoke(CaptureDiscardReason.Cancelled);
    }

    public override void _Input(InputEvent @event)
    {
        if (!HandlePushToTalkInput || @event is null || !@event.IsAction(InputBindings.PushToTalk)) return;

        if (@event.IsPressed() && !@event.IsEcho())
        {
            if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
            if (BeginCapture()) GetViewport().SetInputAsHandled();
        }
        else if (@event.IsReleased() && IsCapturing)
        {
            EndCapture();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        // A release that happens while the window is unfocused never arrives; end the take instead of recording on.
        if (what == NotificationApplicationFocusOut && IsCapturing) EndCapture();
    }

    public override void _Process(double delta)
    {
        if (!IsCapturing) return;

        var available = _capture.GetFramesAvailable();
        if (available <= 0) return;

        var frames = _capture.GetBuffer(available);
        var room = _maxSourceSamples - _samples.Count;
        var count = Math.Min(frames.Length, room);
        var chunkStart = _samples.Count;
        for (var i = 0; i < count; i++) _samples.Add((frames[i].X + frames[i].Y) * 0.5f);

        InputLevel = PcmLevel.ToMeter(PcmLevel.Rms(CollectionsMarshal.AsSpan(_samples)[chunkStart..]));
        _peakMeter = Math.Max(_peakMeter, InputLevel);

        if (_samples.Count >= _maxSourceSamples) Finish(truncated: true);
    }

    private void Finish(bool truncated)
    {
        if (!IsCapturing) return;
        StopTake();

        var seconds = (double)_samples.Count / _sourceRateHz;
        if (seconds < MinUtteranceSeconds)
        {
            _samples.Clear();
            CaptureDiscarded?.Invoke(CaptureDiscardReason.TooShort);
            return;
        }

        var samples = _samples.ToArray();
        _samples.Clear();
        // EncodeAndPublishAsync never faults: it logs its own failures.
        _ = EncodeAndPublishAsync(samples, _sourceRateHz, seconds, truncated, _peakMeter, _takeGeneration);
    }

    private async Task EncodeAndPublishAsync(float[] samples, int sourceRateHz, double seconds, bool truncated, float peakMeter, int generation)
    {
        try
        {
            var wav = await Task.Run(() => WavEncoder.EncodePcm16Mono(AudioResampler.Resample(samples, sourceRateHz, TargetSampleRateHz), TargetSampleRateHz));
            // Cancelled, disabled or frozen while encoding: the take no longer counts.
            if (!IsInstanceValid(this) || generation != _takeGeneration) return;
            _log?.Debug(Category, string.Create(CultureInfo.InvariantCulture, $"take {seconds:0.00}s truncated={truncated} peak={peakMeter:0.00}"));
            PublishUtterance(new CapturedUtterance(wav, seconds, truncated, peakMeter));
        }
        catch (Exception ex)
        {
            _log?.Error(Category, $"take not published: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // One throwing subscriber must not keep the others from receiving the take.
    private void PublishUtterance(CapturedUtterance utterance)
    {
        if (UtteranceCaptured is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<CapturedUtterance>)handler)(utterance);
            }
            catch (Exception ex)
            {
                _log?.Error(Category, $"UtteranceCaptured subscriber failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void StopTake()
    {
        IsCapturing = false;
        InputLevel = 0f;
        UpdateStream();
    }

    private void UpdateStream()
    {
        if (_player is null) return;
        var shouldPlay = _micEnabled && (_listening || IsCapturing);
        if (shouldPlay && !_player.Playing) _player.Play();
        else if (!shouldPlay && _player.Playing) _player.Stop();
    }
}
