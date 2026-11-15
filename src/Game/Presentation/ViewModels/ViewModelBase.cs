namespace Usa.Game.Presentation.ViewModels;

/// <summary>
/// Base for view models: owns the subscriptions it makes and releases them, newest first, on <see cref="Dispose()"/>.
/// </summary>
public abstract class ViewModelBase : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    protected bool IsDisposed { get; private set; }

    /// <summary>Takes ownership of <paramref name="disposable"/>; disposed at once if this view model already is.</summary>
    protected TDisposable Track<TDisposable>(TDisposable disposable)
        where TDisposable : IDisposable
    {
        ArgumentNullException.ThrowIfNull(disposable);
        if (IsDisposed) disposable.Dispose();
        else _owned.Add(disposable);
        return disposable;
    }

    protected void Observe<T>(IReadOnlyObservable<T> source, Action<T> onChanged, bool invokeImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        Track(source.Subscribe(onChanged, invokeImmediately));
    }

    protected void Observe<T>(ObservableList<T> source, Action<ListChange<T>> onChanged)
    {
        ArgumentNullException.ThrowIfNull(source);
        Track(source.Subscribe(onChanged));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed) return;
        IsDisposed = true;
        if (!disposing) return;
        for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }
}
