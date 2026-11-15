using Usa.Simulation.Kernel;

namespace Usa.Simulation.Ports;

public enum EffectOperation
{
    /// <summary>Replace the field's value.</summary>
    Set,

    /// <summary>Add a number to the field.</summary>
    Add,

    /// <summary>Multiply the field by a number.</summary>
    Multiply,
}

/// <summary>
/// A data-driven state change (incident effect, task result, mission rule). The value is an expression
/// so data can reference difficulty or variants, e.g. <c>@difficulty.stuck_thrust_fraction</c>.
/// </summary>
/// <param name="Target">A state path without '@', e.g. <c>tandem/oams/thruster_08.valve_open</c>.</param>
public sealed record EffectDefinition(string Target, EffectOperation Operation, IExpression Value);

/// <summary>Applies effects for the state it owns. Registered with <see cref="EffectRouter"/> by prefix.</summary>
public interface IEffectHandler
{
    string Prefix { get; }

    bool CanApply(string target, EffectOperation operation);

    void Apply(string target, EffectOperation operation, StateValue value, MissionTime now);
}

/// <summary>Why <see cref="EffectRouter.ApplyAll"/> applied nothing.</summary>
public enum EffectBatchRefusal
{
    None,

    /// <summary>No handler accepts the target and operation.</summary>
    Unroutable,

    /// <summary>The value, or the field's value after Add/Multiply, is not a finite number.</summary>
    NonFinite,
}

/// <summary>Outcome of <see cref="EffectRouter.ApplyAll"/>: every effect applied, or none and the first that failed.</summary>
public readonly record struct EffectBatchResult(EffectBatchRefusal Refusal, EffectDefinition? Failed)
{
    public static readonly EffectBatchResult Applied = new(EffectBatchRefusal.None, null);

    public bool IsApplied => Refusal == EffectBatchRefusal.None;
}

/// <summary>The single internal mutation path for data-driven effects.</summary>
public sealed class EffectRouter(IStateQuery state)
{
    private readonly List<IEffectHandler> _handlers = [];

    public void Register(IEffectHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
        _handlers.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
    }

    public bool CanApply(EffectDefinition effect) =>
        Find(effect.Target)?.CanApply(effect.Target, effect.Operation) ?? false;

    /// <exception cref="ArgumentOutOfRangeException">The value, or the field's value after Add/Multiply, is not finite.</exception>
    public void Apply(EffectDefinition effect, MissionTime now)
    {
        var handler = Find(effect.Target) ?? throw new UnknownStatePathException(effect.Target);
        var value = effect.Value.Evaluate(state);
        if (!IsFinite(effect, value))
        {
            // Same exception type the handlers use for non-finite input, so callers see one contract.
            throw new ArgumentOutOfRangeException(nameof(effect),
                $"Effect on '{effect.Target}' evaluated '{effect.Value.Source}' to {value}, or its result is not a finite number.");
        }
        handler.Apply(effect.Target, effect.Operation, value, now);
    }

    /// <summary>
    /// Applies an effect list atomically: every value is evaluated against the state before any effect is applied, and when one
    /// is unroutable (unless <paramref name="skipUnroutable"/>) or not finite (its value or its Add/Multiply result), nothing is
    /// applied. Values are evaluated up front, so a later effect never sees an earlier one's change.
    /// </summary>
    /// <param name="onSkipped">Receives each unroutable effect left out when <paramref name="skipUnroutable"/> is set.</param>
    public EffectBatchResult ApplyAll(IReadOnlyList<EffectDefinition> effects, MissionTime now, bool skipUnroutable = false, Action<EffectDefinition>? onSkipped = null)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (effects.Count == 0) return EffectBatchResult.Applied;
        var handlers = new IEffectHandler?[effects.Count];
        var values = new StateValue[effects.Count];
        for (var i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            var handler = Find(effect.Target);
            if (handler is null || !handler.CanApply(effect.Target, effect.Operation))
            {
                if (!skipUnroutable) return new EffectBatchResult(EffectBatchRefusal.Unroutable, effect);
                continue;
            }
            var value = effect.Value.Evaluate(state);
            if (!IsFinite(effect, value)) return new EffectBatchResult(EffectBatchRefusal.NonFinite, effect);
            handlers[i] = handler;
            values[i] = value;
        }
        for (var i = 0; i < effects.Count; i++)
        {
            if (handlers[i] is { } handler) handler.Apply(effects[i].Target, effects[i].Operation, values[i], now);
            else onSkipped?.Invoke(effects[i]);
        }
        return EffectBatchResult.Applied;
    }

    /// <summary>The value is finite and, for Add/Multiply on a readable number field, so is the combined result.</summary>
    private bool IsFinite(EffectDefinition effect, StateValue value)
    {
        if (value.Kind != StateValueKind.Number) return true;
        var operand = value.AsNumber();
        if (!double.IsFinite(operand)) return false;
        if (effect.Operation == EffectOperation.Set || !state.TryGet(effect.Target, out var current) || current.Kind != StateValueKind.Number)
            return true;
        var result = effect.Operation == EffectOperation.Add ? current.AsNumber() + operand : current.AsNumber() * operand;
        return double.IsFinite(result);
    }

    private IEffectHandler? Find(string target)
    {
        foreach (var handler in _handlers)
        {
            if (target.StartsWith(handler.Prefix, StringComparison.Ordinal)) return handler;
        }
        return null;
    }
}
