using Usa.Simulation.Guidance;

namespace Usa.Game.UI.Console.Binding;

/// <summary>What a gated control should show now: disabled or not, and its tooltip.</summary>
public readonly record struct RailsControlState(bool Disabled, string Tooltip);

/// <summary>
/// The Godot-free half of the console's tutorial-rails gate (<see cref="ITutorialRails"/>): for each actionable control it
/// decides whether the rails block it now ("Follow the guide — step N of 23"), remembers the control's own state while it is
/// blocked and hands it back once the rails allow it again. <typeparamref name="T"/> is the control (a Godot node in the game).
/// </summary>
public sealed class RailsControlGate<T>
    where T : notnull
{
    private readonly Dictionary<T, RailsControlState> _held = [];

    /// <summary>Whether any control is held disabled by the rails.</summary>
    public bool HoldsAny => _held.Count > 0;

    /// <param name="control">The control.</param>
    /// <param name="controlId">Its rails control (<see cref="TutorialControls"/>); null for one the rails never gate.</param>
    /// <param name="own">The control's own state now.</param>
    /// <returns>The state to apply; null to leave the control as it is.</returns>
    public RailsControlState? Gate(T control, string? controlId, ITutorialRails? rails, RailsControlState own)
    {
        var blocked = controlId is not null && rails is { IsEngaged: true } && !rails.IsAllowed(controlId);
        if (blocked)
        {
            _held.TryAdd(control, own);
            return new RailsControlState(true, rails!.BlockedTooltip);
        }
        return _held.Remove(control, out var saved) ? saved : null;
    }

    /// <summary>Drops controls that no longer exist.</summary>
    public void Forget(Func<T, bool> gone)
    {
        ArgumentNullException.ThrowIfNull(gone);
        foreach (var control in _held.Keys.Where(gone).ToArray()) _held.Remove(control);
    }
}
