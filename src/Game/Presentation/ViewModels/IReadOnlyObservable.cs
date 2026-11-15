namespace Usa.Game.Presentation.ViewModels;

/// <summary>A value UI controls can read and bind to but not change.</summary>
public interface IReadOnlyObservable<out T>
{
    /// <summary>The latest value; with throttling this can be ahead of the last notification.</summary>
    T Value { get; }

    /// <summary>Calls <paramref name="onChanged"/> with the new value after each published change.</summary>
    /// <param name="invokeImmediately">Also call it once now with the current value, so bindings start in sync.</param>
    /// <returns>Dispose to unsubscribe.</returns>
    IDisposable Subscribe(Action<T> onChanged, bool invokeImmediately = true);
}
