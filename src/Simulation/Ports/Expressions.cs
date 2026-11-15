namespace Usa.Simulation.Ports;

/// <summary>A compiled data expression (trigger, precondition, feasibility rule, hint condition).</summary>
public interface IExpression
{
    string Source { get; }

    /// <summary>Every state path the expression reads (without the '@'), for load-time validation.</summary>
    IReadOnlyList<string> Paths { get; }

    StateValue Evaluate(IStateQuery state);
}

/// <summary>Compiles expression source once at load. Implemented by the Expressions area.</summary>
/// <remarks>
/// Grammar: numbers, 'text', true/false, @paths, unary - and not, * /, + -, comparisons
/// (&lt; &lt;= &gt; &gt;= == !=), and, or, parentheses, and the functions any, all, abs, min, max, clamp.
/// </remarks>
public interface IExpressionCompiler
{
    /// <exception cref="ExpressionException">Syntax error, with the column.</exception>
    IExpression Compile(string source);
}

public sealed class ExpressionException(string expressionSource, int column, string message)
    : Exception($"{message} at column {column} in \"{expressionSource}\"")
{
    public string ExpressionSource { get; } = expressionSource;
    public int Column { get; } = column;
}

public static class ExpressionExtensions
{
    public static bool IsTrue(this IExpression expression, IStateQuery state) => expression.Evaluate(state).AsBool();

    /// <summary>Paths in <paramref name="expression"/> that no provider knows.</summary>
    public static IEnumerable<string> UnknownPaths(this IExpression expression, IStateQuery state) =>
        expression.Paths.Where(path => !state.IsKnownPath(path));
}
