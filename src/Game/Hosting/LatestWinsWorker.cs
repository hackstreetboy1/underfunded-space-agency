namespace Usa.Game.Hosting;

/// <summary>
/// Runs work items on the thread pool one at a time, in order. An item posted while another runs waits; a later post
/// replaces the waiting one (latest wins), so a slow disk never builds a backlog of stale writes. Post from one thread.
/// </summary>
public sealed class LatestWinsWorker
{
    private readonly Action<Exception> _onError;
    private readonly Lock _gate = new();
    private Action? _waiting;
    private Task _running = Task.CompletedTask;
    private bool _busy;

    /// <param name="onError">Called on the worker thread when an item throws; the next item still runs.</param>
    public LatestWinsWorker(Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(onError);
        _onError = onError;
    }

    /// <summary>True while an item runs or waits.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate) return _busy;
        }
    }

    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            if (_busy)
            {
                _waiting = work;
                return;
            }
            _busy = true;
            _running = Task.Run(() => Drain(work));
        }
    }

    /// <summary>Drops the waiting item, if any, and blocks until the running one finished (before a synchronous write or delete).</summary>
    public void CancelWaitingAndWait()
    {
        Task running;
        lock (_gate)
        {
            _waiting = null;
            running = _running;
        }
        running.Wait();
    }

    /// <summary>Blocks until every posted item ran (shutdown).</summary>
    public void WaitIdle()
    {
        while (true)
        {
            Task running;
            lock (_gate)
            {
                if (!_busy) return;
                running = _running;
            }
            running.Wait();
        }
    }

    private void Drain(Action first)
    {
        Action? work = first;
        try
        {
            while (work is not null)
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    _onError(ex);
                }
                lock (_gate)
                {
                    work = _waiting;
                    _waiting = null;
                    if (work is null) _busy = false;
                }
            }
        }
        finally
        {
            // Only when the error callback itself threw: never leave the worker marked busy.
            if (work is not null)
            {
                lock (_gate)
                {
                    _busy = false;
                    _waiting = null;
                }
            }
        }
    }
}
