using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Usa.Game.Services.Ai.Contracts;
using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Ai;

/// <summary>
/// Typed client for the local proxy (SCRIPTING.md 13.1, 14.5). Never holds provider keys.
/// Each endpoint has its own attempt timeout; up to two retries with jittered backoff reuse the same
/// <c>request_id</c>, so the proxy returns its cached result instead of calling a provider twice.
/// Failures come back as <see cref="AiResult{T}"/> values, never as exceptions, also after <see cref="Dispose"/> (calls then
/// end as <see cref="AiError.Cancelled"/>, so shutdown never reads as an outage). Arguments that are bugs rather than
/// failures (a null request, a request id the proxy would reject, see <see cref="IsValidRequestId"/>) throw
/// <see cref="ArgumentException"/> before anything is sent. Redirects are not followed, so a request never leaves the
/// loopback proxy. Every timeout and backoff runs on the injected <see cref="TimeProvider"/>.
/// <para>
/// Threading: a call's result arrives on the caller's synchronization context (Godot's main thread for main-thread
/// callers). <see cref="OutageSuspected"/> is always raised on the synchronization context that was current when the
/// gateway was constructed (<c>GameRoot</c> builds it on the main thread), whichever thread made the failing call;
/// with no context at construction (tests, tools) it is raised inline on the failing call's thread.
/// </para>
/// </summary>
public sealed class AiGateway : IAiHealthSource, IDisposable
{
    public const string DefaultBaseUrl = "http://127.0.0.1:8787/";

    /// <summary>Mirrors <c>REQUEST_ID_MAX_LENGTH</c> in <c>backend/app/schemas/common.py</c>.</summary>
    public const int MaxRequestIdLength = 128;

    internal const int MaxAudioResponseBytes = 16 * 1024 * 1024;

    private const string Category = "AiGateway";
    private const int InitialAudioBufferBytes = 64 * 1024;
    private const int AudioReadChunkBytes = 16 * 1024;
    private static readonly MediaTypeHeaderValue WavMediaType = new("audio/wav");

    private readonly HttpClient _http;
    private readonly Log _log;
    private readonly Func<double> _jitter;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _eventContext = SynchronizationContext.Current;

    // Cancelled (never disposed) by Dispose, so in-flight calls end as Cancelled instead of failing on a disposed client.
    private readonly CancellationTokenSource _shutdown = new();
    private Uri _baseUrl;

    /// <param name="handler">Test seam; production uses a proxy-less <see cref="SocketsHttpHandler"/>.</param>
    /// <param name="jitter">Uniform [0, 1) source for backoff jitter.</param>
    public AiGateway(Uri baseUrl, Log log, HttpMessageHandler? handler = null, Func<double>? jitter = null, TimeProvider? time = null)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _log = log;
        _jitter = jitter ?? Random.Shared.NextDouble;
        _time = time ?? TimeProvider.System;
        _http = handler is null
            ? new HttpClient(CreateLoopbackHandler())
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc/>
    public event Action<AiError>? OutageSuspected;

    public Uri BaseUrl => Volatile.Read(ref _baseUrl);

    /// <summary>Points later calls at a new proxy URL (settings change). In-flight calls finish against the old one.</summary>
    public void SetBaseUrl(Uri baseUrl) => Volatile.Write(ref _baseUrl, NormalizeBaseUrl(baseUrl));

    /// <summary>A fresh id for one logical request. Keep it for every retry or re-send after a freeze.</summary>
    public static string NewRequestId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// The proxy's idempotency-key rule (<c>REQUEST_ID_PATTERN</c> in <c>backend/app/schemas/common.py</c>): 1 to
    /// <see cref="MaxRequestIdLength"/> characters from <c>A-Z a-z 0-9 _ . : -</c>. Any other id is answered 422.
    /// </summary>
    public static bool IsValidRequestId([NotNullWhen(true)] string? requestId)
    {
        if (requestId is null || requestId.Length is 0 or > MaxRequestIdLength) return false;
        foreach (var c in requestId)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '-')) return false;
        }
        return true;
    }

    public Task<AiResult<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
        SendAsync(AiEndpoint.Health, requestId: "-",
            () => new HttpRequestMessage(HttpMethod.Get, Url(AiEndpoint.Health)),
            ReadJson(AiJsonContext.Default.HealthResponse), cancellationToken);

    /// <param name="wav">PCM16 WAV bytes (see <c>WavEncoder</c>); the proxy rejects anything without a RIFF/WAVE header.</param>
    public Task<AiResult<SttResponse>> TranscribeAsync(string requestId, ReadOnlyMemory<byte> wav, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidRequestId(requestId);
        return SendAsync(AiEndpoint.Stt, requestId, () =>
        {
            var audio = new ReadOnlyMemoryContent(wav);
            audio.Headers.ContentType = WavMediaType;
            var form = new MultipartFormDataContent
            {
                { audio, "audio", "utterance.wav" },
                { new StringContent(requestId), "request_id" },
            };
            return new HttpRequestMessage(HttpMethod.Post, Url(AiEndpoint.Stt)) { Content = form };
        }, ReadJson(AiJsonContext.Default.SttResponse), cancellationToken);
    }

    public Task<AiResult<InterpretResponse>> InterpretAsync(InterpretRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync(AiEndpoint.Interpret, request, AiJsonContext.Default.InterpretRequest, AiJsonContext.Default.InterpretResponse, cancellationToken);

    public Task<AiResult<NarrateResponse>> NarrateAsync(NarrateRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync(AiEndpoint.Narrate, request, AiJsonContext.Default.NarrateRequest, AiJsonContext.Default.NarrateResponse, cancellationToken);

    public Task<AiResult<TipResponse>> TipAsync(TipRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync(AiEndpoint.Tip, request, AiJsonContext.Default.TipRequest, AiJsonContext.Default.TipResponse, cancellationToken);

    public Task<AiResult<ExplainResponse>> ExplainAsync(ExplainRequest request, CancellationToken cancellationToken = default) =>
        PostJsonAsync(AiEndpoint.Explain, request, AiJsonContext.Default.ExplainRequest, AiJsonContext.Default.ExplainResponse, cancellationToken);

    /// <summary>Returns MP3 bytes for <c>AudioStreamMP3.Data</c>.</summary>
    public Task<AiResult<byte[]>> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalidRequestId(request.RequestId);
        return SendAsync(AiEndpoint.Tts, request.RequestId,
            () => new HttpRequestMessage(HttpMethod.Post, Url(AiEndpoint.Tts))
            {
                Content = JsonContent.Create(request, AiJsonContext.Default.TtsRequest),
            },
            ReadMp3, cancellationToken);
    }

    public bool IsDisposed => _shutdown.IsCancellationRequested;

    public void Dispose()
    {
        if (IsDisposed) return;
        _shutdown.Cancel();
        _http.Dispose();
    }

    /// <summary>The production handler: no system proxy, and no redirects, so a 3xx from the port is an error, not a hop.</summary>
    internal static SocketsHttpHandler CreateLoopbackHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        // Loopback accepts in well under a millisecond; Windows takes ~2 s to report a refused port.
        ConnectTimeout = TimeSpan.FromMilliseconds(750),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    private Task<AiResult<TResponse>> PostJsonAsync<TRequest, TResponse>(
        AiEndpoint endpoint,
        TRequest request,
        JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
        where TRequest : IAiRequest
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalidRequestId(request.RequestId);
        return SendAsync(endpoint, request.RequestId,
            () => new HttpRequestMessage(HttpMethod.Post, Url(endpoint)) { Content = JsonContent.Create(request, requestType) },
            ReadJson(responseType), cancellationToken);
    }

    private static void ThrowIfInvalidRequestId(string? requestId, [CallerArgumentExpression(nameof(requestId))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(requestId, paramName);
        if (!IsValidRequestId(requestId))
            throw new ArgumentException($"Request id must be 1-{MaxRequestIdLength} characters from A-Z a-z 0-9 _ . : -.", paramName);
    }

    private async Task<AiResult<T>> SendAsync<T>(
        AiEndpoint endpoint,
        string requestId,
        Func<HttpRequestMessage> createRequest,
        Func<HttpContent, CancellationToken, Task<T?>> readBody,
        CancellationToken cancellationToken)
        where T : class
    {
        if (IsDisposed || cancellationToken.IsCancellationRequested) return AiResult.Failure<T>(AiError.Cancelled, 0);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        try
        {
            return await SendCoreAsync(endpoint, requestId, createRequest, readBody, call.Token);
        }
        catch (Exception ex) when (call.IsCancellationRequested)
        {
            _log.Debug(Category, $"{endpoint.Name} req={requestId} ended by {ex.GetType().Name} after cancellation");
            return AiResult.Failure<T>(AiError.Cancelled, 0);
        }
        catch (Exception ex)
        {
            // The contract is "never throws": an unexpected fault is logged loudly and reported as unreachable.
            _log.Error(Category, $"{endpoint.Name} req={requestId} failed unexpectedly: {ex.GetType().Name}: {ex.Message}");
            return AiResult.Failure<T>(AiError.Unreachable, 0);
        }
    }

    /// <param name="cancellationToken">The caller's token linked with shutdown: when it fires the call ends as Cancelled.</param>
    private async Task<AiResult<T>> SendCoreAsync<T>(
        AiEndpoint endpoint,
        string requestId,
        Func<HttpRequestMessage> createRequest,
        Func<HttpContent, CancellationToken, Task<T?>> readBody,
        CancellationToken cancellationToken)
        where T : class
    {
        var started = _time.GetTimestamp();
        var outcome = default(AttemptOutcome<T>);
        var attempt = 0;
        while (attempt < endpoint.MaxAttempts)
        {
            if (attempt > 0)
            {
                try
                {
                    await Task.Delay(RetryBackoff.Delay(attempt, _jitter(), outcome.RetryAfter), _time, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return AiResult.Failure<T>(AiError.Cancelled, attempt);
                }
            }

            attempt++;
            outcome = await AttemptAsync(endpoint, createRequest, readBody, cancellationToken);
            if (outcome.Value is not null)
            {
                _log.Debug(Category, $"{endpoint.Name} req={requestId} ok attempt={attempt} ms={ElapsedMs(started)}");
                return AiResult.Success(outcome.Value, attempt);
            }

            var error = outcome.Error!;
            if (error.Kind == AiFailureKind.Cancelled) return AiResult.Failure<T>(error, attempt);
            // Health failures are the monitor's own signal; re-raising them would make it probe in a loop.
            if (error.IndicatesOutage && endpoint != AiEndpoint.Health) RaiseOutageSuspected(error);
            if (!error.Retryable) break;
        }

        var failure = outcome.Error!;
        _log.Warn(Category, $"{endpoint.Name} req={requestId} failed code={failure.ErrorCode} status={failure.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "-"} attempts={attempt} ms={ElapsedMs(started)}");
        return AiResult.Failure<T>(failure, attempt);
    }

    private void RaiseOutageSuspected(AiError error)
    {
        if (_eventContext is null || SynchronizationContext.Current == _eventContext)
        {
            NotifyOutageSuspected(error);
            return;
        }
        _eventContext.Post(static state =>
        {
            var (gateway, posted) = ((AiGateway, AiError))state!;
            gateway.NotifyOutageSuspected(posted);
        }, (this, error));
    }

    // One throwing subscriber must neither fault the call nor keep the others from hearing about the outage.
    private void NotifyOutageSuspected(AiError error)
    {
        if (OutageSuspected is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<AiError>)handler)(error);
            }
            catch (Exception ex)
            {
                _log.Error(Category, $"OutageSuspected subscriber failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <param name="cancellationToken">Fires on caller cancellation or shutdown; anything it ends is <see cref="AiError.Cancelled"/>.</param>
    private async Task<AttemptOutcome<T>> AttemptAsync<T>(
        AiEndpoint endpoint,
        Func<HttpRequestMessage> createRequest,
        Func<HttpContent, CancellationToken, Task<T?>> readBody,
        CancellationToken cancellationToken)
        where T : class
    {
        HttpResponseMessage response;
        using (var headersDeadline = new CancellationTokenSource(endpoint.AttemptTimeout, _time))
        using (var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, headersDeadline.Token))
        {
            using var request = createRequest();
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                if (cancellationToken.IsCancellationRequested) return AttemptOutcome<T>.Failed(AiError.Cancelled);
                // Neither token fired: the handler's connect timeout did, i.e. nothing is listening.
                return AttemptOutcome<T>.Failed(headersDeadline.IsCancellationRequested ? AiError.ClientTimeout : AiError.Unreachable);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                return AttemptOutcome<T>.Failed(cancellationToken.IsCancellationRequested ? AiError.Cancelled : AiError.Unreachable);
            }
        }

        using (response)
        using (var bodyDeadline = new CancellationTokenSource(endpoint.BodyTimeout, _time))
        using (var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bodyDeadline.Token))
        {
            try
            {
                if (!response.IsSuccessStatusCode)
                    return AttemptOutcome<T>.Failed(await ReadErrorAsync(response, bodyTimeout.Token), response.Headers.RetryAfter?.Delta);

                var value = await readBody(response.Content, bodyTimeout.Token);
                return value is null ? AttemptOutcome<T>.Failed(AiError.InvalidResponse) : new AttemptOutcome<T>(value, null, null);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                if (cancellationToken.IsCancellationRequested) return AttemptOutcome<T>.Failed(AiError.Cancelled);
                // Only the body deadline is a client timeout; a stream torn down under us is a lost connection.
                return AttemptOutcome<T>.Failed(bodyDeadline.IsCancellationRequested ? AiError.ClientTimeout : AiError.Unreachable);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                // Not the contract: bad JSON, an unknown charset or an unsupported content type.
                return AttemptOutcome<T>.Failed(AiError.InvalidResponse);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                return AttemptOutcome<T>.Failed(cancellationToken.IsCancellationRequested ? AiError.Cancelled : AiError.Unreachable);
            }
        }
    }

    private static async Task<AiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var fallbackRetryable = status >= 500 || response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout;
        try
        {
            var body = await response.Content.ReadFromJsonAsync(AiJsonContext.Default.ErrorResponse, cancellationToken);
            if (body is not null) return new AiError(body.ErrorCode, body.Retryable, AiFailureKind.Http, status);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Not the proxy's error shape (e.g. something else on the port); fall back to the status code.
        }
        return new AiError($"http_{status}", fallbackRetryable, AiFailureKind.Http, status);
    }

    private static Func<HttpContent, CancellationToken, Task<T?>> ReadJson<T>(JsonTypeInfo<T> type)
        where T : class =>
        (content, ct) => content.ReadFromJsonAsync(type, ct);

    private static async Task<byte[]?> ReadMp3(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentType?.MediaType is not "audio/mpeg") return null;
        var declared = content.Headers.ContentLength;
        if (declared is > MaxAudioResponseBytes) return null;
        await using var body = await content.ReadAsStreamAsync(cancellationToken);
        var bytes = await ReadAtMostAsync(body, MaxAudioResponseBytes, (int)(declared ?? InitialAudioBufferBytes), cancellationToken);
        return bytes is { Length: > 0 } ? bytes : null;
    }

    /// <summary>
    /// Reads the whole stream, or returns null as soon as it exceeds <paramref name="maxBytes"/>: a chunked body has no
    /// length to check up front, and a declared length can lie.
    /// </summary>
    private static async Task<byte[]?> ReadAtMostAsync(Stream stream, int maxBytes, int expectedBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(Math.Clamp(expectedBytes, 0, maxBytes));
        var chunk = ArrayPool<byte>.Shared.Rent(AudioReadChunkBytes);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(0, AudioReadChunkBytes), cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes) return null;
                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
        return buffer.ToArray();
    }

    private Uri Url(AiEndpoint endpoint) => new(BaseUrl, endpoint.Path);

    private static Uri NormalizeBaseUrl(Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (!baseUrl.IsAbsoluteUri) throw new ArgumentException("Proxy URL must be absolute.", nameof(baseUrl));
        return baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
    }

    private long ElapsedMs(long startedTimestamp) => (long)_time.GetElapsedTime(startedTimestamp).TotalMilliseconds;

    private readonly record struct AttemptOutcome<T>(T? Value, AiError? Error, TimeSpan? RetryAfter)
        where T : class
    {
        public static AttemptOutcome<T> Failed(AiError error, TimeSpan? retryAfter = null) => new(null, error, retryAfter);
    }
}
