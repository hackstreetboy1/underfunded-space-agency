namespace Usa.Simulation.Ports;

/// <summary>A compiled expression met state it can't evaluate: an unknown path or a value of the wrong kind.</summary>
/// <remarks>Lives in Ports so every area can catch it by type.</remarks>
public sealed class ExpressionEvaluationException(string expressionSource, int column, string message)
    : InvalidOperationException($"{message} at column {column} in expression \"{expressionSource}\"")
{
    public string ExpressionSource { get; } = expressionSource;

    public int Column { get; } = column;
}
