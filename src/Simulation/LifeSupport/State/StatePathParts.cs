using Usa.Simulation.Ports;

namespace Usa.Simulation.LifeSupport.State;

internal static class StatePathParts
{
    /// <summary>
    /// Splits the text after <paramref name="prefix"/> at its last '.' into owner and field without allocating.
    /// The owner is empty for "prefix.field".
    /// </summary>
    public static bool TrySplit(string path, string prefix, out ReadOnlySpan<char> owner, out ReadOnlySpan<char> field)
    {
        owner = default;
        field = default;
        if (path is null || !path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = path.AsSpan(prefix.Length);
        var dot = rest.LastIndexOf('.');
        owner = dot < 0 ? ReadOnlySpan<char>.Empty : rest[..dot];
        field = dot < 0 ? rest : rest[(dot + 1)..];
        return !field.IsEmpty && (dot < 0 || !owner.IsEmpty);
    }

    /// <exception cref="ArgumentOutOfRangeException">The operand or the result is NaN or infinite.</exception>
    public static double Combine(string target, EffectOperation operation, double current, double operand)
    {
        if (!double.IsFinite(operand))
            throw new ArgumentOutOfRangeException(nameof(operand), operand, $"Effect on '{target}' needs a finite number, got {operand}.");
        var result = operation switch
        {
            EffectOperation.Set => operand,
            EffectOperation.Add => current + operand,
            EffectOperation.Multiply => current * operand,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
        return double.IsFinite(result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(operand), result, $"Effect {operation} on '{target}' gives non-finite {result}.");
    }
}
