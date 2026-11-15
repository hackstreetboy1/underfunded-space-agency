using Godot;
using Usa.Game.Presentation.Cues;
using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;
using Usa.Game.Services.Connection;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Voice;
using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms.Hosting;

/// <summary>
/// Wires the comms pipeline to the client services: push-to-talk capture → transcription, connection loss → hold
/// (requests and speech synthesis held, capture cancelled; playback itself follows the app freeze), speech requests →
/// TTS → per-crew playback with captions. Exposes C# events for the comms panel. Call <see cref="Initialize"/> before
/// adding it to the tree. Subscriptions follow the tree (enter/exit), so a re-parented controller keeps working. When the
/// orchestrator is disposed (the run stops) this run's queued and playing crew speech is stopped at once, and only lines
/// this controller delivered ever reach <see cref="CaptionStarted"/>; the orchestrator is also disposed with the node.
/// With <see cref="AttachRadio"/> the same lines and the push-to-talk key drive the radio chirps and static bed.
/// </summary>
public sealed partial class CommsController : Node
{
    private const string Category = "Comms";
    private const double PollSeconds = 0.25;

    private CommsOrchestrator _comms = null!;
    private AiGateway _gateway = null!;
    private ConnectionMonitor _connection = null!;
    private MicCapture _mic = null!;
    private SpeechPlayer _speech = null!;
    private Log _log = null!;
    private CrewSpeechSequencer _sequencer = null!;
    private Godot.Timer? _poll;
    private CancellationTokenSource? _lifetime;
    private readonly CaptionGate<SpeechLine> _captions = new();
    private RadioCueMapper? _radio;

    public event Action<TranscriptChange>? TranscriptChanged;

    public event Action<ConfirmationRequest>? ConfirmationRequested;

    public event Action<ConfirmationClosed>? ConfirmationClosed;

    public event Action<PttState>? PttStateChanged;

    /// <summary>A low-confidence transcript to place in the input field with its flagged words highlighted.</summary>
    public event Action<TranscriptReview>? ReviewRequested;

    /// <summary>
    /// A line started playing (or, caption-only, started showing): show its caption now and hold it for
    /// <see cref="CaptionCue.HoldSeconds"/> (the clip or reading time plus the 1.5 s tail).
    /// </summary>
    public event Action<CaptionCue>? CaptionStarted;

    public CommsOrchestrator Comms => _comms;

    /// <summary>The input bar's addressee selector: a crew member, or null for "Crew".</summary>
    public CrewId? Addressee { get; set; }

    public void Initialize(CommsOrchestrator comms, AiGateway gateway, ConnectionMonitor connection, MicCapture mic, SpeechPlayer speech, Log log)
    {
        _comms = comms;
        _gateway = gateway;
        _connection = connection;
        _mic = mic;
        _speech = speech;
        _log = log;
        _sequencer = new CrewSpeechSequencer(SynthesizeAsync, Deliver, OnFaulted);
    }

    /// <summary>
    /// Plays the radio sounds around speech through <paramref name="radio"/>: PTT chirps when the player's key goes down and
    /// up, and a keyed transmission (chirps and static bed) around every crew line this controller delivered. Chirps hold
    /// while speech is held (freeze, outage); the radio is reset when the run stops or the controller leaves the tree.
    /// </summary>
    public void AttachRadio(RadioCueMapper radio)
    {
        ArgumentNullException.ThrowIfNull(radio);
        _radio?.Reset();
        _radio = radio;
    }

    public override void _EnterTree()
    {
        if (_comms is null) throw new InvalidOperationException($"{nameof(CommsController)}.{nameof(Initialize)} must be called before adding it to the tree.");

        Name = nameof(CommsController);
        _lifetime = new CancellationTokenSource();

        _comms.Transcript.Changed += OnTranscriptChanged;
        _comms.ConfirmationRequested += OnConfirmationRequested;
        _comms.ConfirmationClosed += OnConfirmationClosed;
        _comms.PttStateChanged += OnPttStateChanged;
        _comms.ReviewRequested += OnReviewRequested;
        _comms.SpeechRequested += OnSpeechRequested;
        _comms.Diagnostic += OnDiagnostic;
        _comms.Faulted += OnFaulted;
        _mic.CaptureStarted += _comms.OnPushToTalkStarted;
        _mic.UtteranceCaptured += OnUtteranceCaptured;
        _mic.CaptureDiscarded += OnCaptureDiscarded;
        _connection.Lost += OnConnectionLost;
        _connection.Restored += OnConnectionRestored;
        _speech.PlaybackStarted += OnPlaybackStarted;
        _speech.PlaybackFinished += OnPlaybackFinished;
        _comms.Disposed += OnCommsDisposed;

        _comms.SetMicEnabled(_mic.MicEnabled);
        if (_connection.IsLost) OnConnectionLost(_connection.LastReport);
    }

    public override void _Ready()
    {
        _poll = new Godot.Timer { WaitTime = PollSeconds, OneShot = false, Autostart = true };
        _poll.Timeout += OnPoll;
        AddChild(_poll);
    }

    public override void _ExitTree()
    {
        _comms.Transcript.Changed -= OnTranscriptChanged;
        _comms.ConfirmationRequested -= OnConfirmationRequested;
        _comms.ConfirmationClosed -= OnConfirmationClosed;
        _comms.PttStateChanged -= OnPttStateChanged;
        _comms.ReviewRequested -= OnReviewRequested;
        _comms.SpeechRequested -= OnSpeechRequested;
        _comms.Diagnostic -= OnDiagnostic;
        _comms.Faulted -= OnFaulted;
        _mic.CaptureStarted -= _comms.OnPushToTalkStarted;
        _mic.UtteranceCaptured -= OnUtteranceCaptured;
        _mic.CaptureDiscarded -= OnCaptureDiscarded;
        _connection.Lost -= OnConnectionLost;
        _connection.Restored -= OnConnectionRestored;
        _speech.PlaybackStarted -= OnPlaybackStarted;
        _speech.PlaybackFinished -= OnPlaybackFinished;
        _comms.Disposed -= OnCommsDisposed;
        _radio?.Reset();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
    }

    public override void _Process(double delta)
    {
        if (_radio is null) return;
        _radio.SetHeld(SpeechHeld);
        _radio.Update();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) _comms?.Dispose();
    }

    /// <param name="reviewedVoice">The text is a voice transcript the player checked (and maybe edited).</param>
    public CommandId? SendText(string text, bool reviewedVoice = false) =>
        _comms.SubmitText(text, Addressee, reviewedVoice ? CommandSource.Voice : CommandSource.Text);

    public CommandId? SendQuickCommand(string quickCommandId) => _comms.SubmitQuickCommand(quickCommandId, Addressee);

    /// <summary>The player pressed a crew member's read offer under an "I'd have to check" answer.</summary>
    public CommandId? AcceptOffer(string messageId) => _comms.AcceptOffer(messageId);

    public bool Confirm(string confirmationId) => _comms.Confirm(confirmationId);

    public bool CancelConfirmation(string confirmationId) => _comms.CancelConfirmation(confirmationId);

    public void TouchConfirmation() => _comms.TouchConfirmation();

    public bool CancelPending(string requestId) => _comms.CancelPending(requestId);

    public void DismissReview() => _comms.DismissReview();

    /// <summary>The app freeze (Esc menu, service outage, loading): hold and queue comms. The runtime context normally does this.</summary>
    public void SetFrozen(bool frozen) => _comms.SetFrozen(frozen);

    /// <summary>Route the settings' mic toggle through here so capture and the PTT state agree.</summary>
    public void SetMicEnabled(bool enabled)
    {
        _mic.SetMicEnabled(enabled);
        _comms.SetMicEnabled(enabled);
    }

    private void OnPoll() => _comms.Poll();

    private void OnUtteranceCaptured(CapturedUtterance utterance) => _comms.SubmitUtterance(utterance.Wav, Addressee);

    private void OnCaptureDiscarded(CaptureDiscardReason reason) => _comms.OnCaptureDiscarded();

    private void OnConnectionLost(ConnectionReport report)
    {
        _mic.CancelCapture();
        _sequencer.SetHeld(true);
        _comms.OnConnectionLost();
    }

    private void OnConnectionRestored(ConnectionReport report)
    {
        _sequencer.SetHeld(false);
        _comms.OnConnectionRestored();
    }

    private void OnTranscriptChanged(TranscriptChange change) => TranscriptChanged?.Invoke(change);

    private void OnConfirmationRequested(ConfirmationRequest request) => ConfirmationRequested?.Invoke(request);

    private void OnConfirmationClosed(ConfirmationClosed closed) => ConfirmationClosed?.Invoke(closed);

    private void OnPttStateChanged(PttState state)
    {
        if (_radio is not null)
        {
            _radio.SetHeld(SpeechHeld);
            _radio.SetPlayerKeyed(state == PttState.Listening);
        }

        PttStateChanged?.Invoke(state);
    }

    private void OnReviewRequested(TranscriptReview review) => ReviewRequested?.Invoke(review);

    private void OnPlaybackStarted(SpeechLine line)
    {
        if (!_captions.TryStart(line, _speech.CaptionHoldSeconds(line.CrewId), out var holdSeconds)) return;
        if (_radio is not null)
        {
            _radio.SetHeld(SpeechHeld);
            _radio.LineStarted(line);
        }

        CaptionStarted?.Invoke(new CaptionCue(line, holdSeconds));
    }

    private void OnPlaybackFinished(SpeechFinished finished) =>
        _radio?.LineEnded(finished.Line, cut: finished.Reason == SpeechEndReason.Stopped);

    // Read live, not cached: the app freeze pauses the speech player before it cancels a take, so a key the freeze cut stays
    // silent instead of chirping when the menu closes.
    private bool SpeechHeld => _speech.Paused || _comms.IsFrozen;

    /// <summary>The run stopped: drop its pending lines and silence it before a new run can start speaking.</summary>
    private void OnCommsDisposed()
    {
        _lifetime?.Cancel();
        _speech.StopAll();
        _captions.Clear();
        _radio?.Reset();
    }

    private void OnDiagnostic(string message) => _log.Info(Category, message);

    private void OnFaulted(string where, Exception exception) =>
        _log.Error(Category, $"{where} failed: {exception.GetType().Name}: {exception.Message}");

    private void OnSpeechRequested(CommsSpeechRequest request)
    {
        if (_lifetime is null) return;
        _sequencer.Enqueue(request, _lifetime.Token);
    }

    private async Task<SynthesizedSpeech> SynthesizeAsync(CommsSpeechRequest request, CancellationToken cancellationToken)
    {
        if (request.VoiceId.Length == 0) return new SynthesizedSpeech(null);
        // Text-to-speech is billed by the character: never send more than the spoken cap, whatever built the line.
        var text = SpokenLine.Cap(request.Speech, out var trimmed);
        if (trimmed) _log.Info(Category, $"tts {request.LineId} trimmed to {text.Length} characters");
        if (text.Length == 0) return new SynthesizedSpeech(null);
        var tts = new TtsRequest(request.LineId, request.VoiceId, text) { Cacheable = request.Cacheable };
        var result = await _gateway.SynthesizeAsync(tts, cancellationToken);
        if (result.IsSuccess) return new SynthesizedSpeech(result.Value);
        if (result.Error.Kind != AiFailureKind.Cancelled) _log.Warn(Category, $"tts {request.LineId} failed ({result.Error.ErrorCode}); caption only");
        return new SynthesizedSpeech(null, result.Error.IndicatesOutage);
    }

    private void Deliver(CommsSpeechRequest request, byte[]? mp3)
    {
        if (_lifetime is null || _lifetime.IsCancellationRequested) return;
        var priority = request.Priority switch
        {
            CommsSpeechPriority.Chatter => SpeechPriority.Chatter,
            CommsSpeechPriority.Critical => SpeechPriority.Critical,
            _ => SpeechPriority.Normal,
        };
        var line = new SpeechLine(request.Crew.Value, request.SpeakerName, request.Caption, mp3, priority, request.LineId, request.SpokenText);
        _captions.Track(line);
        _speech.Enqueue(line);
    }
}
