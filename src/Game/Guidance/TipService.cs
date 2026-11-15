using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Commands;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;

namespace Usa.Game.Guidance;

/// <summary>
/// Dynamic tips between the <c>GuidanceDirector</c> and the console tip card (GDD 9.1, SCRIPTING 11.7). A
/// <see cref="TipAvailable"/> shows its authored text at once; the service then may ask <c>/v1/tip</c> to rephrase it
/// (stage, authored text, the tip's own fact labels and bands, its action refs) and swaps the text in only when
/// <see cref="TipRephraseValidator"/> accepts it and the same tip is still on the card. Rephrasings (and rejections) are
/// cached per tip and stage; one request is in flight at a time and requests are spaced by
/// <see cref="TipServiceOptions.RequestCooldown"/>. A <see cref="TipWithdrawn"/> clears the card and cancels its
/// request. Nothing here waits on the AI, pauses time or reaches the grade. Call from one thread; client
/// continuations must return to it (they do when calls start on Godot's main thread). Dispose when the mission stops: the
/// request in flight (or waiting on its spacing) is cancelled and no new one starts.
/// </summary>
public sealed class TipService : IDisposable
{
    private readonly ITipClient _client;
    private readonly TipRephraseValidator _validator;
    private readonly ITipFactSource _facts;
    private readonly ITipGuidanceControl _guidance;
    private readonly TimeSpan _requestCooldown;
    private readonly long _tipCooldownMs;
    private readonly TimeProvider _time;
    private readonly Func<string> _newRequestId;
    private readonly Lock _gate = new();
    private readonly Dictionary<TipKey, CachedText> _cache = [];
    private readonly Dictionary<(TipKey Key, string AuthoredText), string> _requestIds = [];
    private ShownTip? _shown;
    private Pending? _inFlight;
    private long? _lastRequestTimestamp;
    private MissionTime? _cooldownUntil;
    private bool _rephrasingEnabled;
    private bool _disposed;

    /// <param name="tipCooldownS">The hint tree's <c>cooldownS</c>; drives <see cref="CooldownUntil"/> after a dismissal.</param>
    /// <param name="newRequestId">Test seam; defaults to a fresh GUID per tip, stage and authored text.</param>
    public TipService(
        ITipClient client,
        TipRephraseValidator validator,
        ITipFactSource facts,
        ITipGuidanceControl guidance,
        double tipCooldownS,
        TipServiceOptions? options = null,
        TimeProvider? time = null,
        Func<string>? newRequestId = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(guidance);
        if (tipCooldownS < 0 || !double.IsFinite(tipCooldownS)) throw new ArgumentOutOfRangeException(nameof(tipCooldownS), tipCooldownS, "Must be a non-negative number.");
        options ??= TipServiceOptions.Default;
        _client = client;
        _validator = validator;
        _facts = facts;
        _guidance = guidance;
        _requestCooldown = options.RequestCooldown;
        _rephrasingEnabled = options.RephraseEnabled;
        _tipCooldownMs = (long)Math.Round(tipCooldownS * 1000.0);
        _time = time ?? TimeProvider.System;
        _newRequestId = newRequestId ?? (static () => Guid.NewGuid().ToString("N"));
    }

    /// <summary>The card's content or cooldown changed; read <see cref="Card"/> and <see cref="CooldownUntil"/>.</summary>
    public event Action? Changed;

    public event Action<TipRephraseOutcome>? RephraseCompleted;

    /// <summary>Off: tips stay authored and no requests start (an in-flight one is cancelled).</summary>
    public bool RephrasingEnabled
    {
        get
        {
            lock (_gate) return _rephrasingEnabled;
        }
        set
        {
            Pending? launch;
            CancellationTokenSource? cancel;
            lock (_gate)
            {
                _rephrasingEnabled = value;
                launch = value ? TryLaunch() : null;
                cancel = value ? null : _inFlight?.Cancellation;
                // The cancelled request never answers, so the showing must be asked again once rephrasing is back on.
                if (!value && _inFlight is { } pending && _shown is { } shown && shown.Matches(pending.Key, pending.Tip.Text))
                    shown.Requested = false;
            }
            Cancel(cancel);
            Start(launch);
        }
    }

    public bool IsRequestInFlight
    {
        get
        {
            lock (_gate) return _inFlight is not null;
        }
    }

    /// <summary>When the next unsolicited tip may come after a dismissal (the card's "Next tip in …" line); null otherwise.</summary>
    public MissionTime? CooldownUntil
    {
        get
        {
            lock (_gate) return _cooldownUntil;
        }
    }

    /// <summary>Feed every drained simulation event; only tip events matter.</summary>
    public void Observe(ISimEvent simEvent)
    {
        lock (_gate)
        {
            if (_disposed) return;
        }
        switch (simEvent)
        {
            case TipAvailable tip:
                Show(tip);
                break;
            case TipWithdrawn withdrawn:
                Withdraw(withdrawn);
                break;
        }
    }

    /// <summary>The card for the current tip with fact chips as of <paramref name="now"/>, or null when no tip is shown.</summary>
    public TipCardInput? Card(MissionTime now)
    {
        TipAvailable tip;
        string text;
        bool rephrased;
        lock (_gate)
        {
            if (_shown is not { } shown) return null;
            (tip, text, rephrased) = (shown.Tip, shown.Text, shown.Rephrased);
        }

        var chips = new List<TipFactChip>(tip.FactRefs.Count);
        foreach (var fact in tip.FactRefs)
        {
            if (_facts.TryDescribe(fact, now, out var info)) chips.Add(new TipFactChip(fact.Value, info.ChipLabel, info.Knowledge));
        }
        return new TipCardInput(tip.ProblemId, tip.Stage, text, tip.Time, chips) { IsRephrased = rephrased, Requested = tip.Requested };
    }

    /// <summary>The card's ✕. Passed to the director, whose <see cref="TipWithdrawn"/> then clears the card.</summary>
    /// <param name="tipId">The tip the player dismissed; a stale id (the card already moved on) is ignored.</param>
    public bool Dismiss(string? tipId)
    {
        lock (_gate)
        {
            if (_shown is not { } shown) return false;
            if (tipId is not null && !string.Equals(tipId, shown.Tip.ProblemId, StringComparison.Ordinal)) return false;
        }
        return _guidance.Dismiss();
    }

    /// <summary>"Stronger hint", allowed at any time. The director answers with the next stage's <see cref="TipAvailable"/>.</summary>
    public bool RequestStrongerHint() => _guidance.RequestStrongerHint();

    public void Dispose()
    {
        CancellationTokenSource? cancel;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _shown = null;
            cancel = _inFlight?.Cancellation;
        }
        Cancel(cancel);
    }

    private void Show(TipAvailable tip)
    {
        CancellationTokenSource? stale = null;
        Pending? launch;
        lock (_gate)
        {
            var shown = new ShownTip(tip);
            if (_cache.TryGetValue(shown.Key, out var cached) && cached.AuthoredText == tip.Text && cached.Rephrased is { } text)
            {
                shown.Text = text;
                shown.Rephrased = true;
            }
            if (_inFlight is { } pending)
            {
                if (shown.Matches(pending.Key, pending.Tip.Text)) shown.Requested = true;
                else stale = pending.Cancellation;
            }
            _shown = shown;
            _cooldownUntil = null;
            launch = TryLaunch();
        }
        Cancel(stale);
        Changed?.Invoke();
        Start(launch);
    }

    private void Withdraw(TipWithdrawn withdrawn)
    {
        CancellationTokenSource? stale = null;
        lock (_gate)
        {
            if (withdrawn.Reason == TipWithdrawReason.Dismissed) _cooldownUntil = withdrawn.Time + _tipCooldownMs;
            if (_shown is { } shown && string.Equals(shown.Tip.ProblemId, withdrawn.ProblemId, StringComparison.Ordinal))
            {
                _shown = null;
                stale = _inFlight?.Cancellation;
            }
        }
        Cancel(stale);
        Changed?.Invoke();
    }

    /// <summary>Claims the request slot for the shown tip when it still needs a rephrasing. Caller holds the gate.</summary>
    private Pending? TryLaunch()
    {
        if (_disposed || !_rephrasingEnabled || _inFlight is not null || _shown is not { Requested: false } shown) return null;
        shown.Requested = true;
        if (_cache.TryGetValue(shown.Key, out var cached) && cached.AuthoredText == shown.Tip.Text) return null;

        var delay = TimeSpan.Zero;
        if (_lastRequestTimestamp is { } last && _time.GetElapsedTime(last) is var elapsed && elapsed < _requestCooldown)
            delay = _requestCooldown - elapsed;
        _inFlight = new Pending(shown.Key, shown.Tip, delay, new CancellationTokenSource());
        return _inFlight;
    }

    /// <summary>
    /// One id per tip, stage and authored text, so asking again after a failure is a retry the gateway can answer
    /// from its idempotency cache instead of a second provider call. Caller holds the gate.
    /// </summary>
    private string RequestIdFor(Pending pending)
    {
        var key = (pending.Key, pending.Tip.Text);
        if (!_requestIds.TryGetValue(key, out var id)) _requestIds[key] = id = _newRequestId();
        return id;
    }

    private void Start(Pending? pending)
    {
        if (pending is not null) _ = RunAsync(pending);
    }

    private async Task RunAsync(Pending pending)
    {
        HintCandidate? authored = null;
        AiResult<TipResponse>? result = null;
        try
        {
            var token = pending.Cancellation.Token;
            if (pending.Delay > TimeSpan.Zero) await WaitAsync(pending.Delay, token);
            string requestId;
            lock (_gate)
            {
                if (_shown is not { } shown || !shown.Matches(pending.Key, pending.Tip.Text)) return;
                _lastRequestTimestamp = _time.GetTimestamp();
                requestId = RequestIdFor(pending);
            }

            var request = TipRequests.Build(pending.Tip, requestId, _facts);
            authored = request.Candidates[0];
            result = await _client.TipAsync(request, token);
        }
        catch (OperationCanceledException)
        {
            // Superseded or withdrawn while waiting; the card already shows the authored text.
        }
        finally
        {
            Complete(pending, authored, result);
        }
    }

    /// <summary>Like <c>Task.Delay</c> on the time provider, but completes on the thread that fires or cancels it; the await still resumes on the caller's context.</summary>
    private async Task WaitAsync(TimeSpan delay, CancellationToken token)
    {
        var elapsed = new TaskCompletionSource();
        using var timer = _time.CreateTimer(static state => ((TaskCompletionSource)state!).TrySetResult(), elapsed, delay, Timeout.InfiniteTimeSpan);
        using var registration = token.Register(static state => ((TaskCompletionSource)state!).TrySetCanceled(), elapsed);
        await elapsed.Task;
    }

    private void Complete(Pending pending, HintCandidate? authored, AiResult<TipResponse>? result)
    {
        TipRephraseOutcome? outcome = null;
        var changed = false;
        Pending? next;
        bool disposed;
        lock (_gate)
        {
            if (ReferenceEquals(_inFlight, pending)) _inFlight = null;
            disposed = _disposed;
            if (authored is not null && result is not null && result.Error?.Kind != AiFailureKind.Cancelled)
            {
                IReadOnlyList<AiRejection> rejections = [];
                if (result.IsSuccess)
                {
                    var response = result.Value;
                    rejections = _validator.Validate(response, authored);
                    // A radio habit alone ("CAPCOM, check ...") should not cost a good rephrasing: drop it and judge again.
                    if (TipRephraseValidator.OnlyCallSignOpener(rejections) &&
                        TipRephraseValidator.WithoutCallSignOpener(response.Text) is { } stripped)
                    {
                        response = response with { Text = stripped };
                        rejections = _validator.Validate(response, authored);
                    }
                    var text = rejections.Count == 0 ? response.Text.Trim() : null;
                    if (text == pending.Tip.Text) text = null;
                    _cache[pending.Key] = new CachedText(pending.Tip.Text, text);
                    if (text is not null && _shown is { } shown && shown.Matches(pending.Key, pending.Tip.Text))
                    {
                        shown.Text = text;
                        shown.Rephrased = true;
                        changed = true;
                    }
                }
                outcome = new TipRephraseOutcome(pending.Key.TipId, pending.Key.Stage, result.Error, rejections, changed);
            }
            next = TryLaunch();
        }

        pending.Cancellation.Dispose();
        if (disposed) return;
        if (changed) Changed?.Invoke();
        if (outcome is not null) RephraseCompleted?.Invoke(outcome);
        Start(next);
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request completed between the decision and the cancel; nothing left to stop.
        }
    }

    private readonly record struct TipKey(string TipId, HintStage Stage);

    /// <param name="Rephrased">Null when the answer was rejected or unchanged: the authored text stands for this key.</param>
    private sealed record CachedText(string AuthoredText, string? Rephrased);

    private sealed record Pending(TipKey Key, TipAvailable Tip, TimeSpan Delay, CancellationTokenSource Cancellation);

    private sealed class ShownTip(TipAvailable tip)
    {
        public TipAvailable Tip { get; } = tip;

        public TipKey Key { get; } = new(tip.ProblemId, tip.Stage);

        public string Text { get; set; } = tip.Text;

        public bool Rephrased { get; set; }

        /// <summary>A rephrase was requested (or is not needed) for this showing; never asked twice.</summary>
        public bool Requested { get; set; }

        /// <summary>Alternative wordings share a key, so the authored text is part of the identity.</summary>
        public bool Matches(TipKey key, string authoredText) => Key == key && string.Equals(Tip.Text, authoredText, StringComparison.Ordinal);
    }
}
