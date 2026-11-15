using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>Audio for one line: MP3 bytes, or null for caption-only.</summary>
/// <param name="Retryable">The speech service looked unreachable, so the line may be synthesized again after an outage.</param>
public readonly record struct SynthesizedSpeech(byte[]? Mp3, bool Retryable = false);

/// <summary>
/// Orders each crew member's spoken lines. Synthesis starts at once; delivery waits for the same speaker's previous
/// line, so lines never swap order. A link that fails or throws never blocks the links after it: that line falls
/// back to caption-only and the fault is reported. A line whose synthesis failed as retryable waits for the outage
/// hold (<see cref="SetHeld"/>), which usually starts a few seconds after the failure that revealed it; if the hold
/// starts within <see cref="DefaultOutageGrace"/> the line is synthesized once more, with the same request, after the
/// release. Otherwise it is captioned without audio.
/// </summary>
/// <remarks>Single-threaded: call it from the main thread.</remarks>
public sealed class CrewSpeechSequencer
{
    /// <summary>Longer than the connection monitor needs to confirm an outage (an immediate probe plus one heartbeat).</summary>
    public static readonly TimeSpan DefaultOutageGrace = TimeSpan.FromSeconds(7);

    private readonly Func<CommsSpeechRequest, CancellationToken, Task<SynthesizedSpeech>> _synthesize;
    private readonly Action<CommsSpeechRequest, byte[]?> _deliver;
    private readonly Action<string, Exception> _faulted;
    private readonly TimeProvider _time;
    private readonly TimeSpan _outageGrace;
    private readonly Dictionary<CrewId, Task> _chains = [];
    private TaskCompletionSource? _release;
    private TaskCompletionSource _holdStarted = NewSignal();

    /// <param name="synthesize">Text to speech; may fail or throw.</param>
    /// <param name="deliver">Plays (or, with null audio, captions) the line.</param>
    /// <param name="faulted">An unexpected exception that was recovered from (where, exception).</param>
    /// <param name="time">Clock for <paramref name="outageGrace"/>; the system clock by default.</param>
    /// <param name="outageGrace">How long a retryable failure waits for the outage hold; <see cref="DefaultOutageGrace"/> by default.</param>
    public CrewSpeechSequencer(
        Func<CommsSpeechRequest, CancellationToken, Task<SynthesizedSpeech>> synthesize,
        Action<CommsSpeechRequest, byte[]?> deliver,
        Action<string, Exception> faulted,
        TimeProvider? time = null,
        TimeSpan? outageGrace = null)
    {
        _synthesize = synthesize ?? throw new ArgumentNullException(nameof(synthesize));
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        _faulted = faulted ?? throw new ArgumentNullException(nameof(faulted));
        _time = time ?? TimeProvider.System;
        _outageGrace = outageGrace ?? DefaultOutageGrace;
        ArgumentOutOfRangeException.ThrowIfLessThan(_outageGrace, TimeSpan.Zero, nameof(outageGrace));
    }

    public bool IsHeld => _release is not null;

    public void SetHeld(bool held)
    {
        if (held)
        {
            if (_release is not null) return;
            _release = NewSignal();
            var started = _holdStarted;
            _holdStarted = NewSignal();
            started.TrySetResult();
            return;
        }
        var release = _release;
        _release = null;
        release?.TrySetResult();
    }

    /// <param name="cancellationToken">Cancelled at teardown: pending lines are dropped, not delivered.</param>
    public void Enqueue(CommsSpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var synthesis = SynthesizeAsync(request, cancellationToken);
        var previous = _chains.GetValueOrDefault(request.Crew, Task.CompletedTask);
        _chains[request.Crew] = DeliverInOrderAsync(previous, synthesis, request, cancellationToken);
    }

    /// <summary>Completes when every line enqueued so far has been delivered or dropped. Links never fault.</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_chains.Values.ToArray());

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task<byte[]?> SynthesizeAsync(CommsSpeechRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var speech = await _synthesize(request, cancellationToken);
            if (speech.Mp3 is null && speech.Retryable && await WaitForOutageEndAsync(cancellationToken))
                speech = await _synthesize(request, cancellationToken);
            return speech.Mp3;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            _faulted($"tts {request.LineId}", exception);
            return null;
        }
    }

    /// <returns>True when an outage hold covered the failure and has been released; false when none started in time.</returns>
    private async Task<bool> WaitForOutageEndAsync(CancellationToken cancellationToken)
    {
        if (_release is null)
        {
            var started = _holdStarted.Task;
            if (_outageGrace == TimeSpan.Zero) return false;
            using var graceEnded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var grace = Task.Delay(_outageGrace, _time, graceEnded.Token);
            var first = await Task.WhenAny(started, grace);
            await graceEnded.CancelAsync();
            if (first != started) return false;
        }
        if (_release is { } release) await release.Task.WaitAsync(cancellationToken);
        return true;
    }

    private async Task DeliverInOrderAsync(Task previous, Task<byte[]?> synthesis, CommsSpeechRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await previous;
        }
        catch (Exception exception)
        {
            _faulted($"speech chain before {request.LineId}", exception);
        }

        var mp3 = await synthesis;
        if (cancellationToken.IsCancellationRequested) return;
        try
        {
            _deliver(request, mp3);
        }
        catch (Exception exception)
        {
            _faulted($"speech {request.LineId}", exception);
        }
    }
}
