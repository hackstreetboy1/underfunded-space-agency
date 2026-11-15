using Usa.Simulation.Ports;

namespace Usa.Simulation.Expressions;

/// <summary>
/// Compiles the data expression language (grammar in <see cref="IExpressionCompiler"/>) to an AST once;
/// evaluation then allocates nothing.
/// </summary>
/// <remarks>
/// <para>Paths are <c>@</c> followed greedily by <c>[A-Za-z0-9_/.-]+</c>, so division after a path needs a
/// space: <c>@a / 2</c>. Text literals are single-quoted and have no escapes.</para>
/// <para>Typing is strict: arithmetic, ordering, abs, min, max and clamp need numbers; and, or, not, any and all
/// need booleans (a number is never truthy); == and != need both sides of the same kind. Unary minus and
/// <c>not</c> bind tightest, so write <c>not (@a &gt; 1)</c>. Comparisons do not chain. and, or, any and all
/// short-circuit, so a path that is never reached is never read. Kinds known at compile time are checked at
/// compile time; the rest throw <see cref="ExpressionEvaluationException"/> during evaluation.</para>
/// </remarks>
public sealed class ExpressionCompiler : IExpressionCompiler
{
    /// <exception cref="ExpressionException">Syntax or static type error, with its 1-based column.</exception>
    public IExpression Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Parser(source).Parse();
    }
}
