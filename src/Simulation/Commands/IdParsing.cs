namespace Usa.Simulation.Commands;

/// <summary>Non-throwing construction of typed ids from untrusted or data text.</summary>
internal static class IdParsing
{
    public static bool TryCreate<T>(string text, Func<string, T> create, out T id) where T : struct
    {
        try
        {
            id = create(text);
            return true;
        }
        catch (ArgumentException)
        {
            id = default;
            return false;
        }
    }
}
