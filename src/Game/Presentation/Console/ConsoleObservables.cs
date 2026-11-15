using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console;

internal static class ConsoleObservables
{
    /// <summary>
    /// A value that may change every frame (MET, compression, ETA): limited to 10 Hz (UI.md 6.4) when a
    /// throttle is given, immediate otherwise.
    /// </summary>
    public static ObservableValue<T> Throttled<T>(T initial, UpdateThrottle? throttle, IEqualityComparer<T>? comparer = null) =>
        throttle is null
            ? new ObservableValue<T>(initial, comparer)
            : new ObservableValue<T>(initial, throttle, ReadoutViewModel.DefaultMinInterval, comparer);
}
