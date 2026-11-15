using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>
/// Evaluates hint and tutorial conditions. Guidance must never affect play, so a condition that can't be
/// evaluated counts as false instead of throwing out of the tick. Kind errors in data belong at boot, where
/// Session evaluates every compiled expression once.
/// </summary>
internal static class GuidanceConditions
{
    public static bool Holds(IExpression expression, IStateQuery state)
    {
        try
        {
            return expression.IsTrue(state);
        }
        // ExpressionEvaluationException and UnknownStatePathException both derive from it, as does a
        // non-boolean result from StateValue.AsBool.
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
