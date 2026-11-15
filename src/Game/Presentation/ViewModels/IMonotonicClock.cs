namespace Usa.Game.Presentation.ViewModels;

/// <summary>Real (wall) time that only moves forward. Injected so throttles and frame budgets are testable.</summary>
public interface IMonotonicClock
{
    /// <summary>Time since an arbitrary fixed origin.</summary>
    TimeSpan Elapsed { get; }
}
