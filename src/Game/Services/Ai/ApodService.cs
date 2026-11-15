using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Ai;

/// <summary>
/// Fetches the Astronomy Picture of the Day once per game launch, in the background, and keeps it in memory for the
/// loading screens. While the proxy is not answering yet (it may still be starting) the fetch is retried after each of
/// <see cref="RetryDelays"/>; an answer without a picture ends it. Nothing waits on it: readers poll <see cref="Current"/>.
/// </summary>
public sealed class ApodService : IDisposable
{
    private const string Category = "Apod";

    /// <summary>About a minute in all, enough for the proxy's auto-start; every retry only reaches the local proxy.</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];

    private readonly ApodClient _client;
    private readonly Log _log;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task? _run;
    private ApodPicture? _current;
    private int _settled;

    public ApodService(ApodClient client, Log log, TimeProvider? time = null, IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _time = time ?? TimeProvider.System;
        _retryDelays = retryDelays ?? RetryDelays;
    }

    /// <summary>The picture once it has arrived; null before that, or for good when there is none.</summary>
    public ApodPicture? Current => Volatile.Read(ref _current);

    /// <summary>True once the fetch has ended, with or without a picture.</summary>
    public bool Settled => Volatile.Read(ref _settled) != 0;

    /// <summary>Starts the one background fetch; later calls return the same task.</summary>
    public Task Start()
    {
        lock (_gate)
        {
            return _run ??= Task.Run(() => RunAsync(_shutdown.Token));
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _client.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var fetch = await _client.FetchAsync(cancellationToken).ConfigureAwait(false);
                if (fetch.Picture is { } picture)
                {
                    Volatile.Write(ref _current, picture);
                    _log.Info(Category, $"picture of {picture.Info.Date} ready ({picture.Image.Length / 1024} KiB{(picture.Info.IsFallback ? ", earlier day" : "")})");
                    return;
                }

                if (fetch.Status != ApodFetchStatus.Unreachable || attempt >= _retryDelays.Count)
                {
                    _log.Info(Category, $"no picture this launch ({fetch.Status}); loading screens use the curated image");
                    return;
                }

                await Task.Delay(_retryDelays[attempt], _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        finally
        {
            Volatile.Write(ref _settled, 1);
        }
    }
}
