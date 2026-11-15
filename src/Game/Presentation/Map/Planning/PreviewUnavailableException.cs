namespace Usa.Game.Presentation.Map.Planning;

/// <summary>The vehicle cannot be previewed right now (no vehicle, mid-burn or landed).</summary>
public sealed class PreviewUnavailableException : Exception
{
    public PreviewUnavailableException()
    {
    }

    public PreviewUnavailableException(string message) : base(message)
    {
    }

    public PreviewUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
