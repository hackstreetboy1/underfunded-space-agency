using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Game.Comms;

/// <summary>
/// The crew communications pipeline (GDD 5.2, 15.2; SCRIPTING 13): player input (text, voice transcript, quick
/// command) → interpret → <see cref="AiResponseValidator"/> → crew response or <see cref="ICommsSessionPort.Submit"/>
/// → confirmation → result and event narration → fact resolution → speech request.
/// </summary>
/// <remarks>
/// <para>Godot decides, the AI only classifies and phrases: every command goes through the session's validator, and
/// AI text that fails a guardrail is replaced by a deterministic template from <see cref="CommsContent"/>.</para>
/// <para>Freeze (GDD 3.2, 15.6) has two independent inputs: the connection outage (<see cref="OnConnectionLost"/> /
/// <see cref="OnConnectionRestored"/>, which also write the connection lines) and the app's freeze signal
/// (<see cref="SetFrozen"/>: Esc menu, service outage, loading). Comms resumes only when neither holds. While frozen
/// nothing reaches <see cref="ICommsSessionPort.Submit"/> and no AI request starts. A completion that arrives while
/// frozen is kept and applied after the freeze; one that failed because the service was unreachable is re-sent then,
/// with its original id and payload. The player's input (typed or spoken orders, quick commands, read offers, a
/// confirmed dialog) is queued. When the freeze ends everything runs in the order it was held, and queued commands are
/// submitted with the ids they were given. The confirmation auto-cancel countdown (monotonic time) is paused and
/// resumes with its remaining time. A command id is submitted at most once unconfirmed and at most once confirmed,
/// whatever the AI or the network does. After <see cref="Dispose"/> every public call does nothing.</para>
/// <para>GDD 5.2 authority: a question is answered only from facts the speaker knows. Model text with no known fact
/// behind it is never spoken; the crew offers to read the fact instead.</para>
/// <para>Single-threaded: call it from the main thread, and give it an <see cref="IAiClient"/> whose tasks complete there.
/// An unexpected exception inside the pipeline is caught, reported through <see cref="Faulted"/> and answered with a
/// deterministic fallback line, so one failure never stalls comms.</para>
/// </remarks>
public sealed class CommsOrchestrator : IDisposable
{
    public const int MaxNarrationFacts = 16;

    /// <summary>Submitted (command id, confirmed) pairs remembered for de-duplication; far more than can be in flight.</summary>
    public const int MaxRememberedSubmissions = 256;

    private const int MaxEventTextLength = 200;
    private static readonly ActionId ReadFactAction = new("read_fact");
    private static readonly AiError ClientException = new("client_exception", false, AiFailureKind.InvalidResponse);

    private readonly ICommsSessionPort _session;
    private readonly IAiClient _ai;
    private readonly CommsContent _content;
    private readonly ActionCatalog _actions;
    private readonly FactCatalog _facts;
    private readonly CommandIdAllocator _ids;
    private readonly TimeProvider _time;
    private readonly AiResponseValidator _validator;
    private readonly InterpretContextBuilder _context;
    private readonly CrewLineWriter _lines;
    private readonly List<CommsExchange> _exchanges = [];
    private readonly BoundedRecentSet<(CommandId Id, bool Confirmed)> _submitted = new(MaxRememberedSubmissions);
    private readonly HashSet<CommandId> _ackOnTaskStart = [];
    private readonly HashSet<string> _resolvedOffers = new(StringComparer.Ordinal);
    private readonly CommsIntentMatcher _matcher;
    private readonly ITutorialRails? _rails;

    // Clarification message id → the choices the model had in mind (context for the next interpret only, never shown).
    private readonly Dictionary<string, IReadOnlyList<string>> _clarificationOptions = new(StringComparer.Ordinal);

    // Crew lines waiting for the glance reads that fill their values (see AnswerAfterReading), oldest first.
    private readonly List<PendingAnswer> _pendingAnswers = [];
    private const int MaxRememberedClarifications = 16;
    private const int MaxPendingAnswers = 16;
    private readonly List<Task> _work = [];
    private readonly List<HeldWork> _held = [];
    private ConfirmationRequest? _confirmation;

    // The countdown: _confirmationBudget was left at timestamp _confirmationMark; while frozen the mark is not used.
    private TimeSpan _confirmationBudget;
    private long _confirmationMark;
    private PttState _ptt = PttState.Idle;
    private bool _micEnabled = true;
    private bool _connectionLost;
    private bool _hostFrozen;
    private bool _disposed;

    /// <summary>UI.md 5.2: the confirmation dialog auto-cancels after this long without input.</summary>
    public static TimeSpan ConfirmationTimeout { get; } = TimeSpan.FromSeconds(20);

    public CommsOrchestrator(
        ICommsSessionPort session,
        IAiClient ai,
        CommsContent content,
        ActionCatalog actions,
        FactCatalog facts,
        AiGuardrails guardrails,
        CommandIdAllocator ids,
        TimeProvider? time = null,
        ITutorialRails? rails = null)
    {
        _rails = rails;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _time = time ?? TimeProvider.System;

        var view = new CommsValidationView(session);
        _validator = new AiResponseValidator(actions, view, view, view, guardrails ?? throw new ArgumentNullException(nameof(guardrails)));
        _context = new InterpretContextBuilder(session, content, facts);
        _matcher = new CommsIntentMatcher(session, content, facts);
        _lines = new CrewLineWriter(session, content, facts, Transcript, ids);
        _lines.Speak += request => SpeechRequested?.Invoke(request);
        _lines.Diagnostic += Report;
        _session.EventRaised += OnSessionEvent;
    }

    /// <summary>A crew line to voice (TTS) and caption.</summary>
    public event Action<CommsSpeechRequest>? SpeechRequested;

    /// <summary>A high-impact or irreversible action waits for the player (UI.md 5.2).</summary>
    public event Action<ConfirmationRequest>? ConfirmationRequested;

    public event Action<ConfirmationClosed>? ConfirmationClosed;

    public event Action<PttState>? PttStateChanged;

    /// <summary>A low-confidence voice transcript must be checked before it is sent.</summary>
    public event Action<TranscriptReview>? ReviewRequested;

    /// <summary>Why something fell back or was suppressed; for the log, never shown to the player.</summary>
    public event Action<string>? Diagnostic;

    /// <summary>An unexpected exception the pipeline caught and recovered from (where, exception); log it as an error.</summary>
    public event Action<string, Exception>? Faulted;

    /// <summary>The run's comms ended (<see cref="Dispose"/>): hosts stop this run's speech and captions now.</summary>
    public event Action? Disposed;

    public Transcript Transcript { get; } = new();

    public PttState PttState => _ptt;

    /// <summary>The connection is lost or the mission is frozen: AI requests are held and nothing is submitted.</summary>
    public bool IsFrozen => _connectionLost || _hostFrozen;

    public ConfirmationRequest? PendingConfirmation => _confirmation;

    public IReadOnlyList<QuickCommandDefinition> QuickCommands => _content.QuickCommands;

    /// <summary>AI requests in flight or held for the connection.</summary>
    public int PendingRequestCount => _exchanges.Count;

    /// <summary>SCRIPTING 14.5: any word below <see cref="SttResponse.ReviewThreshold"/> forces a transcript review.</summary>
    public static bool NeedsReview(VoiceTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        return transcript.MinWordConfidence < SttResponse.ReviewThreshold
               || transcript.Confidence < SttResponse.ReviewThreshold
               || transcript.LowConfidenceWords.Count > 0;
    }

    /// <summary>
    /// Completes when every AI call started so far (and any started while waiting) has been handled (tests and
    /// shutdown). A faulted call is reported through <see cref="Faulted"/>, never discarded unseen.
    /// </summary>
    public async Task WhenIdleAsync()
    {
        for (PruneWork(); _work.Count > 0; PruneWork())
        {
            // WhenAny never throws: faults are observed and reported by PruneWork.
            await Task.WhenAny(Task.WhenAll(_work.ToArray()));
        }
    }

    /// <summary>A typed order or question (or a reviewed voice transcript, with <paramref name="source"/> Voice).</summary>
    /// <param name="addressee">A crew member, <see cref="CommandRequest.AnyCrew"/> or null for the whole crew.</param>
    /// <returns>The command id, which is also the AI request id; null when the text is empty.</returns>
    public CommandId? SubmitText(string text, CrewId? addressee, CommandSource source = CommandSource.Text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_disposed) return null;
        if (_ptt == PttState.Review) SetPtt(RestingPtt);
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        return TryResolveLocally(trimmed, addressee, source, confidence: null)
               ?? OnRails(trimmed, addressee, source, confidence: null)
               ?? StartInterpret(trimmed, addressee, source, confidence: null);
    }

    /// <summary>
    /// The guided first run's rails, before the AI: the step's own order (its words, or a clear match for its action) goes out as
    /// the step's structured order. Everything else goes to the crew as free speech: questions, reads and chat are answered
    /// normally, and an order the rails don't allow is held back in <see cref="SubmitCommand"/> once the AI has understood it.
    /// </summary>
    private CommandId? OnRails(string text, CrewId? addressee, CommandSource source, double? confidence)
    {
        if (_rails is not { IsEngaged: true, Step: { } step }) return null;
        if (step.Phase == TutorialRailsPhase.Wait || step.Order is not { } order || !NamesOrder(text, order)) return null;
        var id = AppendPlayer(text, addressee, source, confidence, quickCommand: false);
        SubmitCommand(StepRequest(order, id), Alive(order.Crew) ?? Alive(addressee));
        return id;
    }

    private bool NamesOrder(string text, TutorialOrder order)
    {
        var said = CommsIntentMatcher.Normalize(text);
        if (said == CommsIntentMatcher.Normalize(order.Text)) return true;
        return _matcher.Clear(text) is { } match && match.Action.Id == order.Action && !match.IsRead && ArgsAgree(match.Args, order.Args);
    }

    private static bool ArgsAgree(IReadOnlyDictionary<string, CommandArg> said, IReadOnlyDictionary<string, CommandArg> step)
    {
        foreach (var (name, value) in said)
        {
            if (step.TryGetValue(name, out var expected) && !string.Equals(expected.ToCanonicalString(), value.ToCanonicalString(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private CommandRequest StepRequest(TutorialOrder order, CommandId id)
    {
        _actions.TryGet(order.Action, out var action);
        var assignee = action is { UsesCrew: false } ? (CrewId?)null : order.Crew ?? CommandRequest.AnyCrew;
        return new CommandRequest(id, CommandSource.QuickCommand, order.Action, assignee, order.Args, false);
    }

    /// <summary>"Let's stick to the checklist — next is: …", in character, with the step's order as a ready offer when it has one.</summary>
    private void Redirect(string requestId, CrewId? preferred)
    {
        if (_rails is not { Step: { } step } rails) return;
        var speaker = preferred ?? Alive(step.Order?.Crew) ?? DefaultSpeaker(null);
        if (speaker is not { } crew)
        {
            _lines.System(rails.RedirectLine, requestId);
            return;
        }
        ActionOffer? offer = step.Phase != TutorialRailsPhase.Wait && step.Order is { } order
            ? new ActionOffer(StepRequest(order, new CommandId(requestId)), crew, $"Send: ‘{order.Text}’")
            : null;
        _lines.Say(crew, TranscriptMessageKind.Crew, rails.RedirectLine, cacheable: true, requestId: requestId,
            decorate: m => m with { Offer = offer, ReasonCode = OffScriptReason });
    }

    /// <summary>The reason code on the crew's answer to an order the tutorial rails held back.</summary>
    public const string OffScriptReason = "tutorial_off_script";

    /// <summary>A transcript from speech-to-text: sent when confident, otherwise handed back for review.</summary>
    public CommandId? SubmitVoiceTranscript(VoiceTranscript transcript, CrewId? addressee) =>
        _disposed ? null : SubmitVoice(transcript, addressee, fromTranscription: false);

    /// <summary>A push-to-talk recording (16 kHz mono PCM16 WAV): transcribe, then <see cref="SubmitVoiceTranscript"/>.</summary>
    public void SubmitUtterance(ReadOnlyMemory<byte> wav, CrewId? addressee)
    {
        if (_disposed) return;
        SetPtt(PttState.Transcribing);
        Start(new TranscriptionExchange(_ids.NextAuxiliary("stt"), wav, addressee));
    }

    /// <summary>A quick-command button: submitted directly as a command, without the AI.</summary>
    /// <returns>The command id (queued while frozen and submitted with it afterwards); null for an unknown quick command.</returns>
    public CommandId? SubmitQuickCommand(string quickCommandId, CrewId? addressee)
    {
        ArgumentNullException.ThrowIfNull(quickCommandId);
        if (_disposed || !_content.TryGetQuickCommand(quickCommandId, out var quick)) return null;

        var id = AppendPlayer(quick.Label, addressee, CommandSource.QuickCommand, confidence: null, quickCommand: true);
        SubmitQuick(quick, id, addressee);
        return id;
    }

    /// <summary>
    /// The player accepted a crew member's offer (<see cref="TranscriptMessage.Offer"/>): read a fact, or go ahead
    /// with the action CAPCOM asked about. Either way it is submitted as CAPCOM's order, so the session validates it
    /// and a high-impact action still asks for confirmation.
    /// </summary>
    /// <returns>The command id (queued while frozen); null for an unknown or already accepted offer.</returns>
    public CommandId? AcceptOffer(string messageId)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        if (_disposed || Transcript.Find(messageId) is not { Offer: { } offer } || !_resolvedOffers.Add(messageId)) return null;

        var id = AppendPlayer(offer.Label, offer.Speaker, CommandSource.QuickCommand, confidence: null, quickCommand: true);
        SubmitOffer(offer, id);
        return id;
    }

    /// <summary>Whether a crew line's offer is still open (neither accepted nor declined).</summary>
    public bool IsOfferOpen(string messageId) =>
        Transcript.Find(messageId) is { Offer: not null } && !_resolvedOffers.Contains(messageId);

    private void SubmitOffer(CrewOffer offer, CommandId id)
    {
        var request = offer switch
        {
            ReadOffer read => new CommandRequest(id, CommandSource.QuickCommand, ReadFactAction, read.Reader,
                new Dictionary<string, CommandArg>(StringComparer.Ordinal) { ["fact"] = CommandArg.Of(read.Fact.Value) }, false),
            ActionOffer action => action.Command with { Id = id, Source = CommandSource.QuickCommand, Confirmed = false },
            _ => throw new InvalidOperationException($"Unsupported crew offer {offer.GetType().Name}."),
        };
        SubmitCommand(request, offer.Speaker);
    }

    /// <summary>The player cancelled a pending request (✕ on "… is responding"). Nothing it would have done happens.</summary>
    public bool CancelPending(string requestId)
    {
        if (_disposed) return false;
        var exchange = _exchanges.OfType<InterpretExchange>().FirstOrDefault(e => string.Equals(e.Id, requestId, StringComparison.Ordinal));
        if (exchange is null) return false;
        exchange.Cancel();
        exchange.HeldCompletion = null;
        _exchanges.Remove(exchange);
        // An in-flight call disposes the exchange when it settles; otherwise nothing else will.
        if (!exchange.InFlight) exchange.Dispose();
        Transcript.Remove(exchange.PendingMessageId);
        _lines.System(_content.Line(CommsLineKeys.RequestCancelledSystem), exchange.Id);
        return true;
    }

    /// <returns>False for an unknown or expired confirmation. While frozen the confirmed command is queued.</returns>
    public bool Confirm(string confirmationId)
    {
        if (_disposed || _confirmation is not { } confirmation || !string.Equals(confirmation.Id, confirmationId, StringComparison.Ordinal)) return false;
        if (Poll()) return false;
        CloseConfirmation(ConfirmationOutcome.Confirmed);
        SubmitCommand(confirmation.Command with { Confirmed = true }, confirmation.Executor);
        return true;
    }

    public bool CancelConfirmation(string confirmationId)
    {
        if (_disposed || _confirmation is not { } confirmation || !string.Equals(confirmation.Id, confirmationId, StringComparison.Ordinal)) return false;
        CloseConfirmation(ConfirmationOutcome.Cancelled);
        _lines.System(CommsContent.Fill(_content.Line(CommsLineKeys.ConfirmationCancelledSystem), ("action", confirmation.Title)), confirmation.Id);
        return true;
    }

    /// <summary>Player input on the dialog restarts its auto-cancel countdown (from the end of a freeze, if frozen).</summary>
    public void TouchConfirmation()
    {
        if (_disposed || _confirmation is null) return;
        RestartCountdown(ConfirmationTimeout);
    }

    /// <summary>Call regularly (a few times a second): auto-cancels a confirmation after 20 s without input, not counting freezes.</summary>
    /// <returns>True when a confirmation expired.</returns>
    public bool Poll()
    {
        if (_disposed || IsFrozen || _confirmation is not { } confirmation || CountdownRemaining > TimeSpan.Zero) return false;
        CloseConfirmation(ConfirmationOutcome.Expired);
        _lines.System(CommsContent.Fill(_content.Line(CommsLineKeys.ConfirmationExpiredSystem), ("action", confirmation.Title)), confirmation.Id);
        return true;
    }

    public void OnPushToTalkStarted()
    {
        if (_disposed || _ptt == PttState.Disabled || IsFrozen) return;
        SetPtt(PttState.Listening);
    }

    public void OnCaptureDiscarded()
    {
        if (!_disposed && _ptt == PttState.Listening) SetPtt(RestingPtt);
    }

    public void SetMicEnabled(bool enabled)
    {
        if (_disposed) return;
        _micEnabled = enabled;
        if (!enabled) SetPtt(PttState.Disabled);
        else if (_ptt == PttState.Disabled) SetPtt(PttState.Idle);
    }

    /// <summary>The player closed the review without sending.</summary>
    public void DismissReview()
    {
        if (!_disposed && _ptt == PttState.Review) SetPtt(RestingPtt);
    }

    /// <summary>Connection freeze: hold every AI request until <see cref="OnConnectionRestored"/>, with connection lines.</summary>
    public void OnConnectionLost()
    {
        if (_disposed || _connectionLost) return;
        var wasFrozen = IsFrozen;
        _connectionLost = true;
        // A call in flight when the link dropped is asked again after the outage rather than waited for.
        foreach (var exchange in _exchanges)
        {
            if (exchange.InFlight) exchange.ResendOnResume = true;
        }
        if (!wasFrozen) EnterFreeze();
        _lines.System(_content.Line(CommsLineKeys.ConnectionLostSystem));
    }

    /// <summary>The outage ended: run the queue in order with the original ids, unless the mission is still frozen.</summary>
    public void OnConnectionRestored()
    {
        if (_disposed || !_connectionLost) return;
        _connectionLost = false;
        _lines.System(_content.Line(CommsLineKeys.ConnectionRestoredSystem));
        if (!IsFrozen) LeaveFreeze();
    }

    /// <summary>
    /// Follows the app's freeze signal (<c>FreezeController.Frozen</c>: Esc menu, service outage, loading): hold and
    /// queue without the connection lines. It may include the outage; <see cref="OnConnectionLost"/> stays a separate
    /// input, and comms resumes only when both have lifted.
    /// </summary>
    public void SetFrozen(bool frozen)
    {
        if (_disposed || _hostFrozen == frozen) return;
        var wasFrozen = IsFrozen;
        _hostFrozen = frozen;
        if (!wasFrozen && IsFrozen) EnterFreeze();
        else if (wasFrozen && !IsFrozen) LeaveFreeze();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.EventRaised -= OnSessionEvent;
        foreach (var exchange in _exchanges)
        {
            exchange.Cancel();
            exchange.HeldCompletion = null;
            if (!exchange.InFlight) exchange.Dispose();
        }
        _exchanges.Clear();
        _held.Clear();
        Disposed?.Invoke();
    }

    private PttState RestingPtt => _micEnabled ? PttState.Idle : PttState.Disabled;

    /// <summary>Time left on the confirmation countdown; frozen at its budget while comms is frozen.</summary>
    private TimeSpan CountdownRemaining => IsFrozen ? _confirmationBudget : _confirmationBudget - _time.GetElapsedTime(_confirmationMark);

    private void RestartCountdown(TimeSpan budget)
    {
        _confirmationBudget = budget;
        _confirmationMark = _time.GetTimestamp();
    }

    private void EnterFreeze()
    {
        foreach (var exchange in _exchanges) Hold(exchange);
        if (_ptt == PttState.Listening) SetPtt(RestingPtt);
        if (_confirmation is not null)
        {
            var remaining = _confirmationBudget - _time.GetElapsedTime(_confirmationMark);
            _confirmationBudget = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>Resumes the confirmation countdown, then runs the held work in the order it was held.</summary>
    private void LeaveFreeze()
    {
        if (_confirmation is not null) RestartCountdown(_confirmationBudget);

        // One item at a time from the front: if something here freezes comms again, the rest stays queued in order.
        while (_held.Count > 0 && !IsFrozen && !_disposed)
        {
            var work = _held[0];
            _held.RemoveAt(0);
            switch (work)
            {
                case HeldExchange { Exchange: var exchange } when exchange.Held && _exchanges.Contains(exchange):
                    Resume(exchange);
                    break;
                case HeldSubmission submission:
                    SubmitCommand(submission.Request, submission.SpeakerHint);
                    break;
            }
        }
    }

    /// <summary>Applies a kept completion, keeps waiting for a call still in flight, or re-sends the request.</summary>
    private void Resume(CommsExchange exchange)
    {
        exchange.Held = false;
        if (exchange.HeldCompletion is { } apply)
        {
            exchange.HeldCompletion = null;
            _exchanges.Remove(exchange);
            exchange.Dispose();
            apply();
        }
        else if (!exchange.InFlight || exchange.ResendOnResume)
        {
            Start(exchange);
        }
    }

    private void Hold(CommsExchange exchange)
    {
        exchange.Held = true;
        if (!_held.Exists(w => w is HeldExchange held && ReferenceEquals(held.Exchange, exchange))) _held.Add(new HeldExchange(exchange));
    }

    /// <summary>The short "on hold" line: the connection wording only belongs to the outage.</summary>
    private void HeldLine(string requestId) =>
        _lines.System(_content.Line(_connectionLost ? CommsLineKeys.RequestHeldSystem : CommsLineKeys.FrozenSystem), requestId);

    private CommandId? SubmitVoice(VoiceTranscript transcript, CrewId? addressee, bool fromTranscription)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var text = transcript.Text.Trim();
        if (text.Length == 0)
        {
            ResumePtt(RestingPtt, fromTranscription);
            _lines.System(_content.Line(CommsLineKeys.VoiceEmptySystem));
            return null;
        }
        if (NeedsReview(transcript))
        {
            ResumePtt(PttState.Review, fromTranscription);
            ReviewRequested?.Invoke(new TranscriptReview(transcript with { Text = text }, addressee));
            return null;
        }
        ResumePtt(RestingPtt, fromTranscription);
        return TryResolveLocally(text, addressee, CommandSource.Voice, transcript.Confidence)
               ?? StartInterpret(text, addressee, CommandSource.Voice, transcript.Confidence);
    }

    /// <summary>A transcription result never overrides a newer press of the talk key.</summary>
    private void ResumePtt(PttState state, bool fromTranscription)
    {
        if (!fromTranscription || _ptt == PttState.Transcribing) SetPtt(state);
    }

    private CommandId StartInterpret(string text, CrewId? addressee, CommandSource source, double? confidence)
    {
        var id = _ids.Next();
        var recent = RecentLines();
        var openOffers = OpenOffersForContext();
        var openClarification = OpenClarificationForContext();
        var now = _session.Now;
        var kind = source == CommandSource.Voice ? TranscriptMessageKind.PlayerVoice : TranscriptMessageKind.PlayerText;
        Transcript.Append(TranscriptMessage.Draft(kind, now, text) with
        {
            RequestId = id.Value,
            Confidence = confidence,
            Addressee = Specific(addressee),
        });

        var speaker = Alive(addressee);
        var pendingText = speaker is { } named
            ? CommsContent.Fill(_content.Line(CommsLineKeys.Pending), ("name", _content.ShortName(named)))
            : _content.Line(CommsLineKeys.PendingCrew);
        var pending = Transcript.Append(TranscriptMessage.Draft(TranscriptMessageKind.Pending, now, pendingText) with
        {
            RequestId = id.Value,
            Speaker = speaker,
        });

        var request = _context.Build(id.Value, text, addressee, recent, openOffers, openClarification);
        if (IsFrozen) HeldLine(id.Value);
        Start(new InterpretExchange(request, id, source, addressee, pending.Id));
        return id;
    }

    private void Start(CommsExchange exchange)
    {
        if (_disposed) return;
        if (!_exchanges.Contains(exchange)) _exchanges.Add(exchange);
        if (IsFrozen)
        {
            Hold(exchange);
            return;
        }

        exchange.Held = false;
        exchange.InFlight = true;
        exchange.ResendOnResume = false;
        var attempt = ++exchange.Attempt;
        var run = exchange switch
        {
            InterpretExchange interpret => RunInterpretAsync(interpret, attempt),
            NarrationExchange narration => RunNarrationAsync(narration, attempt),
            TranscriptionExchange transcription => RunTranscriptionAsync(transcription, attempt),
            _ => Task.CompletedTask,
        };
        PruneWork();
        _work.Add(run);
        if (run.IsCompleted) PruneWork();
    }

    /// <summary>Drops finished calls and reports any that faulted (each Run*Async body already recovers; this is the backstop).</summary>
    private void PruneWork()
    {
        for (var i = _work.Count - 1; i >= 0; i--)
        {
            var task = _work[i];
            if (!task.IsCompleted) continue;
            _work.RemoveAt(i);
            if (task.Exception is { } failure) Fail("comms call", failure.GetBaseException());
        }
    }

    private void Fail(string where, Exception exception)
    {
        Report($"{where} failed unexpectedly: {exception.GetType().Name}");
        Faulted?.Invoke(where, exception);
    }

    /// <summary>The client contract says "never throws"; if it does anyway, the call becomes an ordinary failure.</summary>
    private async Task<AiResult<T>> CallAsync<T>(Func<Task<AiResult<T>>> call, string where)
        where T : class
    {
        try
        {
            return await call();
        }
        catch (OperationCanceledException)
        {
            return AiResult.Failure<T>(AiError.Cancelled);
        }
        catch (Exception exception)
        {
            Fail(where, exception);
            return AiResult.Failure<T>(ClientException);
        }
    }

    /// <summary>
    /// Acts on a current completion through <paramref name="apply"/>. During a freeze it is kept for the end of the
    /// freeze instead, unless the service was unreachable: then the request is sent again at the end of the freeze.
    /// </summary>
    private void Complete(CommsExchange exchange, int attempt, AiError? failure, Action apply)
    {
        if (attempt == exchange.Attempt) exchange.InFlight = false;
        if (exchange.Cancelled)
        {
            if (attempt == exchange.Attempt) exchange.Dispose();
            return;
        }
        if (_disposed || attempt != exchange.Attempt || !_exchanges.Contains(exchange)) return;
        if (IsFrozen)
        {
            exchange.HeldCompletion = failure is { } error && (error.IndicatesOutage || error.Kind == AiFailureKind.Cancelled) ? null : apply;
            Hold(exchange);
            return;
        }
        _exchanges.Remove(exchange);
        exchange.Dispose();
        apply();
    }

    private static AiError? FailureOf<T>(AiResult<T> result)
        where T : class => result.IsSuccess ? null : result.Error;

    private async Task RunTranscriptionAsync(TranscriptionExchange exchange, int attempt)
    {
        var result = await CallAsync(() => _ai.TranscribeAsync(exchange.Id, exchange.Wav, exchange.Cancellation.Token), $"transcription {exchange.Id}");
        Complete(exchange, attempt, FailureOf(result), () => OnTranscribed(exchange, result));
    }

    private void OnTranscribed(TranscriptionExchange exchange, AiResult<SttResponse> result)
    {
        try
        {
            if (!result.IsSuccess)
            {
                Report($"transcription {exchange.Id} failed: {result.Error.ErrorCode}");
                VoiceFailed();
                return;
            }
            var stt = result.Value;
            SubmitVoice(new VoiceTranscript(stt.Transcript, stt.Confidence, stt.MinWordConfidence, stt.LowConfidenceWords), exchange.Addressee, fromTranscription: true);
        }
        catch (Exception exception)
        {
            Fail($"transcription {exchange.Id}", exception);
            VoiceFailed();
        }
    }

    private void VoiceFailed()
    {
        ResumePtt(RestingPtt, fromTranscription: true);
        _lines.System(_content.Line(CommsLineKeys.VoiceFailedSystem));
    }

    private async Task RunInterpretAsync(InterpretExchange exchange, int attempt)
    {
        var result = await CallAsync(() => _ai.InterpretAsync(exchange.Request, exchange.Cancellation.Token), $"interpret {exchange.Id}");
        Complete(exchange, attempt, FailureOf(result), () => OnInterpreted(exchange, result));
    }

    private void OnInterpreted(InterpretExchange exchange, AiResult<InterpretResponse> result)
    {
        try
        {
            HandleInterpret(exchange, result);
        }
        catch (Exception exception)
        {
            Fail($"interpret {exchange.Id}", exception);
            Transcript.Remove(exchange.PendingMessageId);
            SayLine(Alive(exchange.Addressee), CommsLineKeys.InterpretFailed, exchange.Id);
        }
    }

    private void HandleInterpret(InterpretExchange exchange, AiResult<InterpretResponse> result)
    {
        Transcript.Remove(exchange.PendingMessageId);
        if (!result.IsSuccess)
        {
            Report($"interpret {exchange.Id} failed: {result.Error.ErrorCode}");
            // The model is unavailable, but the player's words may still name one order or value plainly.
            var speaker = Alive(exchange.Addressee) ?? DefaultSpeaker(null);
            if (!ResolveFromWords(exchange, speaker, IsQuestion(exchange.Request.PlayerText)))
                SayLine(Alive(exchange.Addressee), CommsLineKeys.InterpretFailed, exchange.Id);
            return;
        }

        var response = result.Value;
        if (response.Kind == InterpretKind.Question && TryAnswerByReading(response, exchange)) return;

        var validation = _validator.Validate(ToIntent(response), new AiValidationContext(exchange.Command, exchange.Source, exchange.Addressee));
        if (validation.Intent is not { } intent)
        {
            Report($"interpret {exchange.Id} rejected: {string.Join(", ", validation.Rejections)}");
            var speaker = Alive(exchange.Addressee) ?? DefaultSpeaker(null);
            if (UnknownFactAsked(response, validation) is not null && TryAnswerByReading(response, exchange)) return;
            if (!ResolveFromWords(exchange, speaker,
                    response.Kind is InterpretKind.Question or InterpretKind.RecommendationRequest || IsQuestion(exchange.Request.PlayerText)))
                SayLine(Alive(exchange.Addressee), CommsLineKeys.CannotVerify, exchange.Id);
            return;
        }

        switch (intent.Kind)
        {
            case AiIntentKind.Command:
                SubmitCommand(intent.Command!, intent.Speaker);
                break;
            case AiIntentKind.Clarify:
                Clarify(intent, exchange);
                break;
            case AiIntentKind.Chat:
                Chat(intent, exchange);
                break;
            case AiIntentKind.RecommendationRequest:
                Recommend(intent, response, exchange);
                break;
            default:
                Answer(intent, exchange);
                break;
        }
    }

    /// <summary>
    /// A reply that names gauges the crew can read: the speaker reads every one of them fresh (they are the crew's own
    /// checks), then says the model's line with the real values. The line still passes every text guardrail; the only
    /// thing forgiven is that the speaker hasn't read the values yet. False leaves the reply to the normal path.
    /// </summary>
    private bool TryAnswerByReading(InterpretResponse response, InterpretExchange exchange)
    {
        var template = response.DialogueTemplate ?? "";
        var raw = response.FactRefs.Concat(FactTemplateTokens(template)).Distinct(StringComparer.Ordinal).ToArray();
        if (raw.Length == 0) return false;
        var refs = new List<FactId>(raw.Length);
        foreach (var id in raw)
        {
            if (string.IsNullOrWhiteSpace(id) || !_facts.TryResolve(new FactId(id), out _)) return false;
            refs.Add(new FactId(id));
        }
        var readable = GlanceReadPlanner.Readable(_session.AllowedActions).ToHashSet();
        if (!refs.Any(readable.Contains)) return false;

        var speaker = Alive(ParseCrew(response.CrewMember)) ?? DefaultSpeaker(exchange.Addressee);
        if (speaker is not { } crew) return false;

        var check = ToIntent(response) with
        {
            Kind = "question",
            CrewMember = crew.Value,
            FactRefs = refs.Select(f => f.Value).ToArray(),
            DialogueTemplate = template,
            ClarificationQuestion = null,
            ClarificationOptions = null,
        };
        var validation = _validator.Validate(check, new AiValidationContext(exchange.Command, exchange.Source, exchange.Addressee));
        // Unknown facts also count as "token not in refs" in the text checks: both are what the reads are about to fix.
        if (validation.Rejections.Any(r => r.Code != AiRejectionCodes.FactNotKnown
                                           && !(r.Code == AiRejectionCodes.FactTokenNotInRefs && raw.Contains(r.Detail, StringComparer.Ordinal))))
        {
            Report($"interpret {exchange.Id}: read-backed answer rejected: {string.Join(", ", validation.Rejections)}");
            return false;
        }
        if (AiTextChecks.HasDigitOutsideFactTokens(template))
        {
            Report($"interpret {exchange.Id}: digit outside a fact token; reading the facts out instead");
            template = "";
        }
        return AnswerAfterReading(crew, template, refs, exchange.Id);
    }

    private CrewId? ParseCrew(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var member in _session.Crew)
        {
            if (string.Equals(member.Id.Value, raw, StringComparison.Ordinal)) return member.Id;
        }
        return null;
    }

    private async Task RunNarrationAsync(NarrationExchange exchange, int attempt)
    {
        var result = await CallAsync(() => _ai.NarrateAsync(exchange.Request, exchange.Cancellation.Token), $"narration {exchange.Id}");
        Complete(exchange, attempt, FailureOf(result), () => OnNarrated(exchange, result));
    }

    private void OnNarrated(NarrationExchange exchange, AiResult<NarrateResponse> result)
    {
        var template = exchange.FallbackTemplate;
        try
        {
            if (!result.IsSuccess) Report($"narration {exchange.Id} fell back: {result.Error.ErrorCode}");
            else if (RejectNarration(exchange, result.Value) is { } rejection) Report($"narration {exchange.Id} rejected: {rejection}");
            else template = result.Value.DialogueTemplate;

            if (Status(exchange.Speaker) is not { IsAlive: true }) return;
            _lines.Say(exchange.Speaker, exchange.Kind, template, exchange.Priority, data: NarrationData(exchange, template));
        }
        catch (Exception exception)
        {
            Fail($"narration {exchange.Id}", exception);
            if (ReferenceEquals(template, exchange.FallbackTemplate)) return;
            _lines.Say(exchange.Speaker, exchange.Kind, exchange.FallbackTemplate, exchange.Priority, data: exchange.ApprovedFacts);
        }
    }

    /// <summary>
    /// The approved facts the narrated line does not say itself: shown after it with their labels, so the transcript keeps
    /// every value while the spoken line names only the headline.
    /// </summary>
    private static FactId[] NarrationData(NarrationExchange exchange, string template)
    {
        var inline = FactTemplateTokens(template).ToHashSet(StringComparer.Ordinal);
        return exchange.ApprovedFacts.Where(f => !inline.Contains(f.Value)).ToArray();
    }

    private string? RejectNarration(NarrationExchange exchange, NarrateResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.DialogueTemplate)) return "empty_template";
        if (AiTextChecks.HasDigitOutsideFactTokens(response.DialogueTemplate)) return "digit_outside_fact_token";
        foreach (var reference in response.FactRefs)
        {
            if (!exchange.ApprovedFacts.Any(f => string.Equals(f.Value, reference, StringComparison.Ordinal))) return $"fact_not_approved: {reference}";
        }
        var intent = new AiIntent("chat", exchange.Speaker.Value, FactRefs: response.FactRefs, DialogueTemplate: response.DialogueTemplate);
        var validation = _validator.Validate(intent, new AiValidationContext(new CommandId(exchange.Id), CommandSource.Text, exchange.Speaker));
        return validation.IsAccepted ? null : string.Join(", ", validation.Rejections);
    }

    /// <returns>True when the command went through (or was queued behind a freeze); false when it was held back or refused.</returns>
    private bool SubmitCommand(CommandRequest request, CrewId? speakerHint)
    {
        if (IsFrozen)
        {
            _held.Add(new HeldSubmission(request, speakerHint));
            HeldLine(request.Id.Value);
            return true;
        }
        if (_rails is { } rails && !rails.Allows(request))
        {
            Report($"tutorial rails held back {request.Action} ({request.Id})");
            Redirect(request.Id.Value, Alive(request.Assignee) ?? Alive(speakerHint));
            return false;
        }
        if (!_submitted.Add((request.Id, request.Confirmed)))
        {
            Report($"suppressed a second submission of {request.Id} (confirmed={request.Confirmed})");
            return false;
        }

        _actions.TryGet(request.Action, out var action);
        if (action?.Kind == ActionKind.CrewTask) _ackOnTaskStart.Add(request.Id);
        var result = _session.Submit(request);
        if (result.Status != ValidationStatus.Valid) _ackOnTaskStart.Remove(request.Id);

        switch (result.Status)
        {
            case ValidationStatus.Valid:
                Acknowledge(request, action);
                break;
            case ValidationStatus.NeedsConfirmation when result.Impact is { } impact:
                RequestConfirmation(request, action, impact);
                break;
            case ValidationStatus.CrewBusy:
                Busy(request, result);
                break;
            case ValidationStatus.Duplicate:
                Report($"{request.Id} was already executed");
                _lines.System(_content.Reason(result.ReasonCode ?? CommandReasons.DuplicateCommand), request.Id.Value);
                break;
            default:
                Refuse(request, result.ReasonCode, speakerHint);
                break;
        }
        return result.Status is ValidationStatus.Valid or ValidationStatus.NeedsConfirmation;
    }

    /// <summary>GDD 5.2 "acknowledges and executes". Crew tasks are acknowledged by their assignee when they start.</summary>
    private void Acknowledge(CommandRequest request, CatalogAction? action)
    {
        var id = request.Id.Value;
        switch (action?.Kind)
        {
            // A read behind a pending answer was acknowledged once, by that answer.
            case ActionKind.GlanceRead when _pendingAnswers.Any(a => a.Reads.Contains(request.Id)):
                break;
            case ActionKind.GlanceRead:
                SayPhrase(Alive(request.Assignee) ?? DefaultSpeaker(null), CrewPhraseCategory.Standby, id);
                break;
            case ActionKind.Remote:
                _lines.System(CommsContent.Fill(_content.Line(CommsLineKeys.RemoteAppliedSystem), ("action", Title(action))), id);
                break;
            case ActionKind.Meta when action.Definition.Meta == MetaCommandKind.StopTask:
                SayPhrase(BusyMember(null)?.Id ?? DefaultSpeaker(null), CrewPhraseCategory.Stop, id);
                break;
            case ActionKind.Meta when action.Definition.Meta == MetaCommandKind.AcknowledgeAlarms:
                _lines.System(_content.Line(CommsLineKeys.AlarmsAcknowledgedSystem), id);
                break;
            case ActionKind.Meta:
                SayPhrase(Alive(request.Assignee) ?? DefaultSpeaker(null), CrewPhraseCategory.Acknowledge, id);
                break;
        }
    }

    private void RequestConfirmation(CommandRequest request, CatalogAction? action, ImpactSummary impact)
    {
        if (_confirmation is not null) CloseConfirmation(ConfirmationOutcome.Superseded);

        var remote = action is { UsesCrew: false };
        var executor = remote ? null : Alive(request.Assignee);
        var executorName = remote ? _content.Line(CommsLineKeys.ExecutorRemote)
            : executor is { } member ? FullName(member)
            : _content.Line(CommsLineKeys.ExecutorAnyCrew);
        var arguments = request.Args
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => KeyValuePair.Create(a.Key, a.Value.ToCanonicalString()))
            .ToArray();
        var title = action is null ? request.Action.Value : Title(action);
        var confirmation = new ConfirmationRequest(request.Id.Value, request, title, arguments, impact, executor, executorName);

        _confirmation = confirmation;
        RestartCountdown(ConfirmationTimeout);
        _lines.System(CommsContent.Fill(_content.Line(CommsLineKeys.ConfirmationSystem), ("action", title)), confirmation.Id);
        ConfirmationRequested?.Invoke(confirmation);
    }

    private void CloseConfirmation(ConfirmationOutcome outcome)
    {
        if (_confirmation is not { } confirmation) return;
        _confirmation = null;
        ConfirmationClosed?.Invoke(new ConfirmationClosed(confirmation, outcome));
    }

    /// <summary>GDD 5.2 "refuses and gives the reason (from the validator's reason code)".</summary>
    private void Refuse(CommandRequest request, string? reasonCode, CrewId? speakerHint)
    {
        var reason = _content.Reason(reasonCode);
        var speaker = Alive(request.Assignee) ?? Alive(speakerHint) ?? DefaultSpeaker(null);
        if (speaker is not { } crew)
        {
            _lines.System(reason, request.Id.Value);
            return;
        }
        var template = $"{_lines.Phrase(crew, CrewPhraseCategory.Refusal)} {reason}";
        _lines.Say(crew, TranscriptMessageKind.Refusal, template, cacheable: true, requestId: request.Id.Value,
            decorate: m => m with { ReasonCode = reasonCode, ReasonText = reason }, shortSpoken: reason);
    }

    /// <summary>GDD 5.3: the slot is taken; the busy crew member says what they are on (UI.md 3.7).</summary>
    private void Busy(CommandRequest request, ValidationResult result)
    {
        var busy = BusyMember(result.Detail);
        var task = busy?.ActiveTask ?? ParseTask(result.Detail);
        var title = task is { } id ? _session.DescribeTask(id) : result.Detail ?? "";
        var name = busy is null ? _content.Line(CommsLineKeys.ExecutorAnyCrew) : _content.ShortName(busy.Id);
        _lines.System(CommsContent.Fill(_content.Line(CommsLineKeys.BusySystem), ("name", name), ("task", title)), request.Id.Value);
        if (busy is { IsAlive: true })
        {
            _lines.Say(busy.Id, TranscriptMessageKind.Crew, CommsContent.Fill(_content.Line(CommsLineKeys.BusyCrew), ("task", title)),
                cacheable: true, requestId: request.Id.Value);
        }
    }

    private void Answer(ValidatedIntent intent, InterpretExchange exchange)
    {
        if (intent.Speaker is not { } speaker)
        {
            _lines.System(_content.Line(CommsLineKeys.NoCrewSystem), exchange.Id);
            return;
        }
        if (GroundedTemplate(intent, exchange.Id) is { } template)
            _lines.Say(speaker, TranscriptMessageKind.Crew, template, requestId: exchange.Id);
        else if (!ResolveFromWords(exchange, speaker, isQuestion: true))
            // Nothing to go on: ask CAPCOM to put it another way, with no canned order to tap.
            SayLine(speaker, CommsLineKeys.CannotVerify, exchange.Id);
    }

    /// <summary>
    /// GDD 5.2 recommendation request ("should we …?"): never an order, whatever action it names. The crew gives a
    /// view grounded in facts they know and, when the question names an action they could carry out now, asks
    /// "Want us to go ahead?" with an <see cref="ActionOffer"/>. Nothing is submitted unless CAPCOM accepts it.
    /// </summary>
    private void Recommend(ValidatedIntent intent, InterpretResponse response, InterpretExchange exchange)
    {
        if (OfferedCommand(response, exchange) is not { } command)
        {
            Answer(intent, exchange);
            return;
        }
        if (intent.Speaker is not { } speaker)
        {
            _lines.System(_content.Line(CommsLineKeys.NoCrewSystem), exchange.Id);
            return;
        }

        var question = _content.Line(CommsLineKeys.ActionOfferQuestion);
        var view = GroundedTemplate(intent, exchange.Id);
        var template = view is null ? question : view.Contains('?', StringComparison.Ordinal) ? view : $"{view.TrimEnd()} {question}";
        // The button names its target ("Cycle OAMS thruster breakers T1–T4"), so CAPCOM sees exactly what it orders.
        var title = _session.DescribeCommand(command);
        var offer = new ActionOffer(command, speaker, CommsContent.Fill(_content.Line(CommsLineKeys.ActionOffer), ("action", title)));
        _lines.Say(speaker, TranscriptMessageKind.Crew, template, cacheable: view is null, requestId: exchange.Id,
            decorate: m => m with { Offer = offer });
    }

    /// <summary>
    /// The action a recommendation request names, checked as if CAPCOM had ordered it (phase allowlist, parameters,
    /// crew); null when it names none or the order would not be valid, so the crew offers nothing it can't do.
    /// </summary>
    private CommandRequest? OfferedCommand(InterpretResponse response, InterpretExchange exchange)
    {
        if (string.IsNullOrEmpty(response.ActionId)) return null;
        var order = ToIntent(response) with
        {
            Kind = "command",
            FactRefs = null,
            DialogueTemplate = "",
            ClarificationQuestion = null,
            ClarificationOptions = null,
        };
        var validation = _validator.Validate(order, new AiValidationContext(exchange.Command, exchange.Source, exchange.Addressee));
        if (validation.Intent?.Command is { } command) return command;
        Report($"interpret {exchange.Id}: no offer for {response.ActionId}: {string.Join(", ", validation.Rejections)}");
        return null;
    }

    /// <summary>
    /// The crew line for an answer or view: the model's template when known facts back it (GDD 5.2), the plain fact
    /// lines when its wording slipped a digit outside a token, null when no known fact backs it.
    /// </summary>
    private string? GroundedTemplate(ValidatedIntent intent, string requestId)
    {
        if (intent.FactRefs.Count == 0)
        {
            // No fact the speaker knows backs the text, so the model's words are never spoken.
            if (!string.IsNullOrWhiteSpace(intent.DialogueTemplate)) Report($"interpret {requestId}: answer without known facts replaced");
            return null;
        }

        var template = intent.DialogueTemplate;
        if (AiTextChecks.HasDigitOutsideFactTokens(template))
        {
            Report($"interpret {requestId}: digit outside a fact token; reading the facts instead");
            template = "";
        }
        return string.IsNullOrWhiteSpace(template) ? _lines.FactLines(intent.FactRefs) : template;
    }

    /// <summary>
    /// GDD 5.2 "offers to check if they don't": the deterministic "I'd have to check" line, with an offer to read
    /// the fact when <c>read_fact</c> can read it in this phase.
    /// </summary>
    private void OfferToCheck(CrewId? speaker, FactId fact, string requestId)
    {
        if (speaker is not { } crew)
        {
            _lines.System(_content.Line(CommsLineKeys.NoCrewSystem), requestId);
            return;
        }
        var label = Label(fact);
        var offer = CanRead(fact) ? new ReadOffer(fact, crew, CommsContent.Fill(_content.Line(CommsLineKeys.ReadOffer), ("label", label))) : null;
        _lines.Say(crew, TranscriptMessageKind.Crew, CommsContent.Fill(_content.Line(CommsLineKeys.UnknownFactAnswer), ("label", label)),
            cacheable: true, requestId: requestId, decorate: m => m with { Offer = offer });
    }

    /// <summary>
    /// For a question or recommendation request rejected only because the speaker does not know the facts it
    /// names: the first of those facts in the catalog. Null for any other rejection.
    /// </summary>
    private FactId? UnknownFactAsked(InterpretResponse response, AiValidationResult validation)
    {
        if (response.Kind is not (InterpretKind.Question or InterpretKind.RecommendationRequest)) return null;
        FactId? unknown = null;
        foreach (var rejection in validation.Rejections)
        {
            if (rejection.Code == AiRejectionCodes.FactTokenNotInRefs) continue;
            if (rejection.Code != AiRejectionCodes.FactNotKnown) return null;
            if (unknown is null && rejection.Detail is { } raw && _facts.TryResolve(new FactId(raw), out _)) unknown = new FactId(raw);
        }
        return unknown;
    }

    private bool CanRead(FactId fact)
    {
        foreach (var action in _session.AllowedActions)
        {
            if (action.Id != ReadFactAction) continue;
            foreach (var parameter in action.Parameters)
            {
                if (parameter.Values is { } values && values.Contains(fact.Value, StringComparer.Ordinal)) return true;
            }
        }
        return false;
    }

    private void Clarify(ValidatedIntent intent, InterpretExchange exchange)
    {
        if ((intent.Speaker ?? DefaultSpeaker(exchange.Addressee)) is not { } speaker)
        {
            _lines.System(_content.Line(CommsLineKeys.NoCrewSystem), exchange.Id);
            return;
        }
        string? question = intent.ClarificationQuestion;
        if (AiTextChecks.HasDigitOutsideFactTokens(question))
        {
            Report($"interpret {exchange.Id}: clarification has a digit outside a fact token");
            question = null;
        }
        if (string.IsNullOrWhiteSpace(question)) question = null;

        var options = ResolveOptions(intent.ClarificationOptions, speaker, exchange.Id);
        // A generic "which action?" (no real choices, or a list of bare action ids) gives way to the one order or value the
        // player's words clearly name.
        var generic = options.Count < 2 || intent.ClarificationOptions.Count(o => IsAllowedActionId(o.Trim())) >= 2;
        if (generic && ResolveFromWords(exchange, speaker, IsQuestion(exchange.Request.PlayerText))) return;
        AskInWords(speaker, question ?? _content.Line(CommsLineKeys.ClarifyCandidates), options, exchange.Id);
    }

    private bool IsAllowedActionId(string option)
    {
        foreach (var action in _session.AllowedActions)
        {
            if (string.Equals(action.Id.Value, option, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// The crew asks back in their own words and CAPCOM answers however they like: no tap-to-pick replies are shown. The
    /// model's candidate choices are kept only as context, so the next interpret can resolve "the second one" or "ring B".
    /// </summary>
    private void AskInWords(CrewId speaker, string question, List<string> options, string requestId)
    {
        var message = _lines.Say(speaker, TranscriptMessageKind.Clarification, question, requestId: requestId);
        if (options.Count == 0) return;
        if (_clarificationOptions.Count >= MaxRememberedClarifications) _clarificationOptions.Clear();
        _clarificationOptions[message.Id] = options;
    }

    /// <summary>
    /// GDD 5.2 backup when the model is unavailable or its answer was unusable: the player's own words against what can be
    /// ordered now. One clear match is carried out; a value is read and reported; an order named in a question is offered
    /// ("want us to go ahead?"). Never a list of canned replies to pick from. False when nothing clearly matches.
    /// </summary>
    private bool ResolveFromWords(InterpretExchange exchange, CrewId? speaker, bool isQuestion)
    {
        var text = exchange.Request.PlayerText;
        if (_matcher.Clear(text) is not { } clear) return false;
        Report($"interpret {exchange.Id}: resolved from the player's words to {clear.Action.Id}");
        if (clear.Fact is { } fact && speaker is { } reader)
        {
            AnswerAfterReading(reader, "", [fact], exchange.Id);
            return true;
        }
        if (!isQuestion || clear.IsRead)
        {
            SubmitCommand(CandidateRequest(clear, exchange.Command, exchange.Source, exchange.Addressee), speaker);
            return true;
        }
        if (speaker is not { } crew) return false;
        var offer = new ActionOffer(CandidateRequest(clear, exchange.Command, CommandSource.Text, crew), crew,
            CommsContent.Fill(_content.Line(CommsLineKeys.ActionOffer), ("action", clear.Text)));
        _lines.Say(crew, TranscriptMessageKind.Crew, _content.Line(CommsLineKeys.ActionOfferQuestion), cacheable: true, requestId: exchange.Id,
            decorate: m => m with { Offer = offer });
        return true;
    }

    /// <summary>
    /// The user's design: the model writes the crew member's line with <c>&lt;FACT:id&gt;</c> placeholders, and the game
    /// runs the crew's own checks to fill them. Every readable fact the line names is read fresh by <paramref name="speaker"/>
    /// (the gauges' own glance reads, so it takes their usual few seconds); when the last read lands the line is spoken
    /// with the real values. Facts named in <paramref name="factRefs"/> but not placed in the line are read out after it,
    /// with their labels. Returns false, doing nothing, when a fact can be neither read now nor is already known.
    /// </summary>
    private bool AnswerAfterReading(CrewId speaker, string template, IReadOnlyList<FactId> factRefs, string requestId)
    {
        var (reads, unreadable) = GlanceReadPlanner.Plan(_session.AllowedActions, factRefs);
        if (unreadable.Any(f => !_session.Knows(speaker, f))) return false;

        var answer = new PendingAnswer(speaker, template, factRefs, requestId);
        if (_pendingAnswers.Count >= MaxPendingAnswers) _pendingAnswers.RemoveAt(0);
        _pendingAnswers.Add(answer);
        foreach (var read in reads)
        {
            var id = _ids.Next();
            var request = new CommandRequest(id, CommandSource.Text, read.Action.Id, speaker, read.Args, false);
            answer.Reads.Add(id);
            foreach (var fact in read.Facts.Intersect(factRefs)) answer.Missing.Add(fact);
            if (SubmitCommand(request, speaker)) continue;
            // Refused (busy, outside, …): the refusal line already said why; answer from what is known, if anything.
            answer.Reads.Remove(id);
            foreach (var fact in read.Facts) answer.Missing.Remove(fact);
            answer.Failed = true;
        }
        if (answer.Missing.Count == 0)
        {
            _pendingAnswers.Remove(answer);
            FinishAnswer(answer);
        }
        else
        {
            SayPhrase(speaker, CrewPhraseCategory.Standby, requestId);
        }
        return true;
    }

    /// <summary>
    /// New observations by <paramref name="observer"/> fill pending answers; an answer is spoken once all its facts are in.
    /// True when the batch belongs to answers (one of their reads, or only facts they were waiting for), so the stock
    /// read-out is skipped: the answer says it instead, in the model's words.
    /// </summary>
    private bool AbsorbForAnswers(CrewId observer, IEnumerable<FactId> observed, CommandId? origin)
    {
        if (_pendingAnswers.Count == 0) return false;
        var facts = observed.Distinct().ToArray();
        var owned = false;
        var covered = new HashSet<FactId>();
        foreach (var answer in _pendingAnswers.ToArray())
        {
            if (answer.Speaker != observer) continue;
            if (origin is { } id && answer.Reads.Contains(id)) owned = true;
            foreach (var fact in facts)
            {
                if (answer.Missing.Remove(fact)) covered.Add(fact);
            }
            if (answer.Missing.Count > 0) continue;
            _pendingAnswers.Remove(answer);
            FinishAnswer(answer);
        }
        return owned || (facts.Length > 0 && facts.All(covered.Contains));
    }

    private void FinishAnswer(PendingAnswer answer)
    {
        if (Status(answer.Speaker) is not { IsAlive: true }) return;
        var known = answer.Facts.Where(f => _session.Knows(answer.Speaker, f)).ToArray();
        if (known.Length == 0)
        {
            if (!answer.Failed) SayLine(answer.Speaker, CommsLineKeys.CannotVerify, answer.RequestId);
            return;
        }

        var template = answer.Template;
        // Tokens for facts the reads could not produce would fail to resolve; drop the model's line then.
        if (FactTemplateTokens(template).Any(t => !known.Any(k => k.Value == t))) template = "";
        var inline = FactTemplateTokens(template).ToHashSet(StringComparer.Ordinal);
        var rest = known.Where(f => !inline.Contains(f.Value)).ToArray();
        _lines.Say(answer.Speaker, TranscriptMessageKind.Crew, template.Trim(), requestId: answer.RequestId, data: rest);
    }

    private static IEnumerable<string> FactTemplateTokens(string template) =>
        Usa.Simulation.Knowledge.FactTemplateResolver.TokensOf(template);

    /// <summary>A crew line waiting for the glance reads that fill its values.</summary>
    private sealed class PendingAnswer(CrewId speaker, string template, IReadOnlyList<FactId> facts, string requestId)
    {
        public CrewId Speaker { get; } = speaker;

        public string Template { get; } = template;

        public IReadOnlyList<FactId> Facts { get; } = facts;

        public string RequestId { get; } = requestId;

        public HashSet<CommandId> Reads { get; } = [];

        /// <summary>Facts still to come in from the reads.</summary>
        public HashSet<FactId> Missing { get; } = [];

        public bool Failed { get; set; }
    }


    /// <summary>
    /// Input the game can settle without the AI: a short yes or no to the crew's open offer, a quick reply of the open
    /// clarification, a quick command's own words, or "read …" naming one readable value. Null sends it to the AI.
    /// </summary>
    private CommandId? TryResolveLocally(string text, CrewId? addressee, CommandSource source, double? confidence)
    {
        var reply = ShortReply.Classify(text, CrewNameWords());
        if (reply != ShortReplyKind.None && OpenOffer(addressee) is { Offer: { } offer } offered)
        {
            _resolvedOffers.Add(offered.Id);
            var id = AppendPlayer(text, offer.Speaker, source, confidence, quickCommand: false);
            if (reply == ShortReplyKind.Affirmative)
            {
                SubmitOffer(offer, id);
            }
            else if (Alive(offer.Speaker) is { } speaker)
            {
                _lines.Say(speaker, TranscriptMessageKind.Crew, _content.Line(CommsLineKeys.DefaultPhrase(CrewPhraseCategory.Acknowledge)),
                    cacheable: true, requestId: id.Value);
            }
            return id;
        }

        if (_matcher.QuickCommand(text) is { } quick)
        {
            var id = AppendPlayer(text, addressee, source, confidence, quickCommand: true);
            SubmitQuick(quick, id, addressee);
            return id;
        }
        // Everything else, "read …" included, goes to the crew as free speech, so the answer comes back in their own voice.
        return null;
    }

    /// <summary>"…?" or a question word first: a question gets an offer, never an order, from the player's words.</summary>
    private static bool IsQuestion(string text)
    {
        if (text.TrimEnd().EndsWith('?')) return true;
        var first = CommsIntentMatcher.Normalize(text).Split(' ', 2)[0];
        return first is "what" or "whats" or "how" or "hows" or "is" or "are" or "where" or "which" or "did" or "does" or "do";
    }

    private CommandId AppendPlayer(string text, CrewId? addressee, CommandSource source, double? confidence, bool quickCommand)
    {
        var id = _ids.Next();
        var kind = source == CommandSource.Voice ? TranscriptMessageKind.PlayerVoice : TranscriptMessageKind.PlayerText;
        Transcript.Append(TranscriptMessage.Draft(kind, _session.Now, text) with
        {
            RequestId = id.Value,
            Confidence = confidence,
            Addressee = Specific(addressee),
            IsQuickCommand = quickCommand,
        });
        return id;
    }

    private void SubmitQuick(QuickCommandDefinition quick, CommandId id, CrewId? addressee)
    {
        _actions.TryGet(quick.Action, out var action);
        var assignee = action is { UsesCrew: false } ? (CrewId?)null : IsSpecific(addressee) ? addressee : CommandRequest.AnyCrew;
        SubmitCommand(new CommandRequest(id, CommandSource.QuickCommand, quick.Action, assignee, quick.Args ?? CommandRequest.NoArgs, false), Alive(addressee));
    }

    private CommsCandidate? QuickCandidate(QuickCommandDefinition quick)
    {
        if (!_actions.TryGet(quick.Action, out var action)) return null;
        var args = quick.Args ?? CommandRequest.NoArgs;
        FactId? fact = args.TryGetValue("fact", out var raw) ? new FactId(raw.ToCanonicalString()) : null;
        return new CommsCandidate(quick.Label, action, args, fact, 1);
    }

    /// <summary>The order a candidate names, carried out by <paramref name="crew"/> when it names one member.</summary>
    private static CommandRequest CandidateRequest(CommsCandidate candidate, CommandId id, CommandSource source, CrewId? crew)
    {
        var assignee = !candidate.Action.UsesCrew ? (CrewId?)null : IsSpecific(crew) ? crew : CommandRequest.AnyCrew;
        return new CommandRequest(id, source, candidate.Action.Id, assignee, candidate.Args, false);
    }

    /// <summary>
    /// The crew line a short reply answers: the newest line since CAPCOM last spoke with an offer still open, from
    /// the addressed member when one is addressed.
    /// </summary>
    private TranscriptMessage? OpenOffer(CrewId? addressee)
    {
        for (var i = Transcript.Messages.Count - 1; i >= 0; i--)
        {
            var message = Transcript.Messages[i];
            if (message.Kind is TranscriptMessageKind.PlayerText or TranscriptMessageKind.PlayerVoice) return null;
            if (message.Offer is null || _resolvedOffers.Contains(message.Id)) continue;
            if (IsSpecific(addressee) && message.Speaker != addressee) continue;
            return message;
        }
        return null;
    }

    /// <summary>The crew's clarifying question since CAPCOM last spoke, if any.</summary>
    private TranscriptMessage? OpenClarification()
    {
        for (var i = Transcript.Messages.Count - 1; i >= 0; i--)
        {
            var message = Transcript.Messages[i];
            if (message.Kind is TranscriptMessageKind.PlayerText or TranscriptMessageKind.PlayerVoice) return null;
            if (message.Kind == TranscriptMessageKind.Clarification) return message;
        }
        return null;
    }

    /// <summary>Open offers among the recent lines, oldest first, for the model to resolve "yes, and …" or "do the other one".</summary>
    private List<PendingOffer> OpenOffersForContext()
    {
        var offers = new List<PendingOffer>();
        var start = Math.Max(0, Transcript.Messages.Count - InterpretContextBuilder.MaxRecentLines);
        for (var i = start; i < Transcript.Messages.Count; i++)
        {
            var message = Transcript.Messages[i];
            if (message.Offer is not { } offer || _resolvedOffers.Contains(message.Id)) continue;
            offers.Add(offer switch
            {
                ReadOffer read => new PendingOffer(read.Reader.Value, read.Label, ReadFactAction.Value) { Params = [new ParamValue("fact", read.Fact.Value)] },
                ActionOffer action => new PendingOffer(action.Speaker.Value, action.Label, action.Command.Action.Value)
                {
                    Params = action.Command.Args.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new ParamValue(a.Key, a.Value.ToCanonicalString())).ToArray(),
                },
                _ => new PendingOffer(offer.Speaker.Value, offer.Label, ""),
            });
        }
        offers.RemoveAll(o => o.ActionId.Length == 0);
        return offers;
    }

    private PendingClarification? OpenClarificationForContext() =>
        OpenClarification() is { Speaker: { } speaker } message
            ? new PendingClarification(speaker.Value, message.Template ?? message.Text) { Options = _clarificationOptions.GetValueOrDefault(message.Id) ?? message.Options }
            : null;

    private List<string> CrewNameWords()
    {
        var words = new List<string>();
        foreach (var member in _session.Crew)
        {
            if (!_content.TryGetProfile(member.Id, out var profile)) continue;
            foreach (var part in profile.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)) words.Add(CommsIntentMatcher.Normalize(part));
        }
        return words;
    }

    /// <summary>
    /// Quick replies are player-visible text: each passes the same rules as dialogue, and its fact tokens are
    /// resolved from the speaker's observations. An option naming an allowed action by id shows the action's display
    /// name; any other id-like option, or one that fails the rules, is dropped, never shown raw.
    /// </summary>
    private List<string> ResolveOptions(IReadOnlyList<string> options, CrewId speaker, string requestId)
    {
        var resolved = new List<string>(options.Count);
        foreach (var raw in options)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var option = ActionOptionText(raw.Trim());
            if (LooksLikeId(option))
            {
                Report($"interpret {requestId}: dropped a clarification option that is a raw id");
                continue;
            }
            if (AiTextChecks.HasDigitOutsideFactTokens(option))
            {
                Report($"interpret {requestId}: dropped a clarification option with a digit outside a fact token");
                continue;
            }
            var result = _session.ResolveFacts(option, speaker);
            if (!result.Succeeded)
            {
                Report($"interpret {requestId}: dropped a clarification option whose facts did not resolve");
                continue;
            }
            resolved.Add(result.Text.Trim());
        }
        return resolved;
    }

    /// <summary>The display name of the allowed action whose id is <paramref name="option"/>: its task title, else its description.</summary>
    private string ActionOptionText(string option)
    {
        foreach (var action in _session.AllowedActions)
        {
            if (!string.Equals(action.Id.Value, option, StringComparison.Ordinal)) continue;
            return OfferTitle(action);
        }
        return option;
    }

    /// <summary>What the player reads for an action: its task title, else its description.</summary>
    private string OfferTitle(CatalogAction action) =>
        action.Definition.TaskId is { } task ? _session.DescribeTask(task) : Title(action);

    /// <summary>
    /// A single token with an underscore or slash (<c>cycle_oams_breakers</c>, <c>tandem/rcs/ring_a</c>) is an id, not
    /// words; a fact token (<c>&lt;FACT:…&gt;</c>) is not.
    /// </summary>
    private static bool LooksLikeId(string option) =>
        option.AsSpan().IndexOfAny(' ', '<') < 0 && option.AsSpan().IndexOfAny('_', '/') >= 0;

    private void Chat(ValidatedIntent intent, InterpretExchange exchange)
    {
        if ((intent.Speaker ?? DefaultSpeaker(exchange.Addressee)) is not { } speaker) return;
        if (AiTextChecks.HasDigitOutsideFactTokens(intent.DialogueTemplate))
        {
            Report($"interpret {exchange.Id}: chat has a digit outside a fact token");
            SayPhrase(speaker, CrewPhraseCategory.Acknowledge, exchange.Id);
        }
        else if (string.IsNullOrWhiteSpace(intent.DialogueTemplate)) SayPhrase(speaker, CrewPhraseCategory.Acknowledge, exchange.Id);
        else _lines.Say(speaker, TranscriptMessageKind.Crew, intent.DialogueTemplate, CommsSpeechPriority.Chatter, requestId: exchange.Id);
    }

    private void OnSessionEvent(CommsSessionEvent sessionEvent)
    {
        if (_disposed) return;
        switch (sessionEvent)
        {
            case CommsTaskEvent task:
                OnTaskEvent(task);
                break;
            case CommsObservationsRecorded recorded when AbsorbForAnswers(recorded.Observer, recorded.Observations.Select(o => o.Fact), recorded.Origin):
                break;
            case CommsCrewReport crewReport when AbsorbForAnswers(crewReport.Crew, crewReport.Facts, origin: null):
                break;
            case CommsObservationsRecorded observations:
                var facts = observations.Observations.Select(o => o.Fact).Distinct().ToArray();
                Narrate(observations.Observer, Labels(facts), facts, _content.Line(CommsLineKeys.ObservationIntro),
                    TranscriptMessageKind.Crew, CommsSpeechPriority.Normal);
                break;
            case CommsCrewReport report:
                Narrate(report.Crew, report.Summary, report.Facts, _content.Line(CommsLineKeys.CrewReportIntro),
                    TranscriptMessageKind.ObservationReport, report.Critical ? CommsSpeechPriority.Critical : CommsSpeechPriority.Normal);
                break;
        }
    }

    private void OnTaskEvent(CommsTaskEvent task)
    {
        var title = _session.DescribeTask(task.Task);
        var name = _content.ShortName(task.Assignee);
        var origin = task.Origin.Value;
        switch (task.Kind)
        {
            case CommsTaskEventKind.Started:
                _lines.System(SystemLine(CommsLineKeys.TaskStartedSystem, name, title), origin);
                if (_ackOnTaskStart.Remove(task.Origin) && Alive(task.Assignee) is { } assignee)
                    SayPhrase(assignee, CrewPhraseCategory.Acknowledge, origin);
                break;
            case CommsTaskEventKind.Completed:
                _lines.System(SystemLine(CommsLineKeys.TaskCompletedSystem, name, title), origin);
                // Event texts are plain words, never keys: practice mode's stand-in crew reads them back as the line.
                var completed = CommsContent.Fill(_content.Line(CommsLineKeys.TaskCompleted), ("task", title));
                Narrate(task.Assignee, completed.TrimEnd('.'), [], completed, TranscriptMessageKind.Crew, CommsSpeechPriority.Normal);
                break;
            case CommsTaskEventKind.Failed:
                _lines.System(SystemLine(CommsLineKeys.TaskFailedSystem, name, title), origin);
                var failed = CommsContent.Fill(_content.Line(CommsLineKeys.TaskFailed), ("task", title));
                Narrate(task.Assignee, failed.TrimEnd('.'), [], failed, TranscriptMessageKind.Crew, CommsSpeechPriority.Normal);
                break;
            default:
                _ackOnTaskStart.Remove(task.Origin);
                _lines.System(SystemLine(CommsLineKeys.TaskStoppedSystem, name, title), origin);
                break;
        }
    }

    /// <summary>
    /// Narrate (persona + stress) from facts the speaker knows; the fallback is <paramref name="fallbackIntro"/> plus one
    /// fact line per approved fact.
    /// </summary>
    private void Narrate(
        CrewId speaker, string eventText, IReadOnlyList<FactId> facts, string fallbackIntro, TranscriptMessageKind kind, CommsSpeechPriority priority)
    {
        if (Status(speaker) is not { IsAlive: true } status) return;
        var approved = facts.Distinct().Where(f => _session.Knows(speaker, f)).Take(MaxNarrationFacts).ToArray();
        // The fallback is the intro alone: the approved facts follow it as data (every value shown, a summary spoken).
        var fallback = fallbackIntro;

        _content.TryGetProfile(speaker, out var profile);
        var request = new NarrateRequest(_ids.NextAuxiliary("narrate"), speaker.Value, Truncate(eventText, MaxEventTextLength))
        {
            Persona = profile?.Persona,
            Stress = StressLevels.FromHealth(status.HealthPercent),
            Facts = approved.Select(f => new NarrateFact(f.Value, Label(f))).ToArray(),
        };
        Start(new NarrationExchange(request, speaker, approved, fallback, kind, priority));
    }

    private void SayPhrase(CrewId? speaker, CrewPhraseCategory category, string requestId)
    {
        if (speaker is not { } crew) return;
        _lines.Say(crew, TranscriptMessageKind.Crew, _lines.Phrase(crew, category), cacheable: true, requestId: requestId);
    }

    private void SayLine(CrewId? preferred, string lineKey, string requestId)
    {
        if ((preferred ?? DefaultSpeaker(null)) is { } speaker)
            _lines.Say(speaker, TranscriptMessageKind.Crew, _content.Line(lineKey), cacheable: true, requestId: requestId);
        else
            _lines.System(_content.Line(CommsLineKeys.NoCrewSystem), requestId);
    }

    private string SystemLine(string key, string name, string task) => CommsContent.Fill(_content.Line(key), ("name", name), ("task", task));

    /// <summary>The last lines for the AI's context, oldest first. Crew lines use their unresolved template, so no value leaks.</summary>
    private List<string> RecentLines()
    {
        var lines = new List<string>(InterpretContextBuilder.MaxRecentLines);
        for (var i = Transcript.Messages.Count - 1; i >= 0 && lines.Count < InterpretContextBuilder.MaxRecentLines; i--)
        {
            var message = Transcript.Messages[i];
            switch (message.Kind)
            {
                case TranscriptMessageKind.PlayerText or TranscriptMessageKind.PlayerVoice:
                    lines.Add($"Ground: {message.Text}");
                    break;
                case TranscriptMessageKind.Crew or TranscriptMessageKind.Clarification or TranscriptMessageKind.Refusal or TranscriptMessageKind.ObservationReport:
                    lines.Add($"{message.SpeakerName}: {message.Template ?? message.Text}");
                    break;
            }
        }
        lines.Reverse();
        return lines;
    }

    private CommsCrewStatus? Status(CrewId crew)
    {
        foreach (var member in _session.Crew)
        {
            if (member.Id == crew) return member;
        }
        return null;
    }

    /// <summary>The member if it names one crew member, living or not; null for the whole crew.</summary>
    private static CrewId? Specific(CrewId? crew) => IsSpecific(crew) ? crew : null;

    /// <summary>The member if it names a specific, living crew member; otherwise null.</summary>
    private CrewId? Alive(CrewId? crew) =>
        IsSpecific(crew) && Status(crew!.Value) is { IsAlive: true } ? crew : null;

    /// <summary>The addressee if alive, else the first living free member, else the first living member (roster order).</summary>
    private CrewId? DefaultSpeaker(CrewId? addressee)
    {
        if (Alive(addressee) is { } named) return named;
        CrewId? firstAlive = null;
        foreach (var member in _session.Crew)
        {
            if (!member.IsAlive) continue;
            if (!member.IsBusy) return member.Id;
            firstAlive ??= member.Id;
        }
        return firstAlive;
    }

    private CommsCrewStatus? BusyMember(string? taskDetail)
    {
        CommsCrewStatus? busy = null;
        foreach (var member in _session.Crew)
        {
            if (!member.IsBusy) continue;
            if (member.ActiveTask is { } task && string.Equals(task.Value, taskDetail, StringComparison.Ordinal)) return member;
            busy ??= member;
        }
        return busy;
    }

    private string FullName(CrewId crew) => _content.TryGetProfile(crew, out var profile) ? profile.Name : crew.Value;

    private string Label(FactId fact) => _facts.TryResolve(fact, out var definition) ? definition.Label : fact.Value;

    private string Labels(IReadOnlyList<FactId> facts) => string.Join(", ", facts.Select(Label));

    private void SetPtt(PttState state)
    {
        if (_ptt == state) return;
        _ptt = state;
        PttStateChanged?.Invoke(state);
    }

    private void Report(string message) => Diagnostic?.Invoke(message);

    private static string Title(CatalogAction action) => action.Definition.Description.TrimEnd('.');

    private static bool IsSpecific(CrewId? crew) => crew is { } id && id != CommandRequest.AnyCrew;

    private static TaskId? ParseTask(string? detail)
    {
        if (string.IsNullOrEmpty(detail)) return null;
        try
        {
            return new TaskId(detail);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    private static AiIntent ToIntent(InterpretResponse response) => new(
        response.Kind switch
        {
            InterpretKind.Command => "command",
            InterpretKind.Question => "question",
            InterpretKind.Clarify => "clarify",
            InterpretKind.RecommendationRequest => "recommendation_request",
            InterpretKind.Chat => "chat",
            _ => "unknown",
        },
        response.CrewMember,
        response.ActionId,
        response.Params.Select(p => new AiParamValue(p.Name, p.Value)).ToArray(),
        response.FactRefs,
        response.DialogueTemplate,
        response.ClarificationQuestion,
        response.ClarificationOptions,
        response.RequiresConfirmation);

    /// <summary>Work queued during a freeze, run in this order when it ends.</summary>
    private abstract record HeldWork;

    private sealed record HeldExchange(CommsExchange Exchange) : HeldWork;

    private sealed record HeldSubmission(CommandRequest Request, CrewId? SpeakerHint) : HeldWork;
}
