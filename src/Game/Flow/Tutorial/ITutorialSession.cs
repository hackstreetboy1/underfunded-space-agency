namespace Usa.Game.Flow.Tutorial;

/// <summary>
/// The running session's guided first run, as the callout layer sees it. Every change is a session command (it lands in the
/// command log, so a replay or resume repeats it); reads are plain state.
/// </summary>
public interface ITutorialSession
{
    /// <summary>The session's tutorial director runs (difficulty default, then the logged on/off commands).</summary>
    bool IsEnabled { get; }

    /// <summary>The callout on screen now, or null; used once when a resumed run attaches.</summary>
    TutorialCallout? Active { get; }

    /// <summary>Submits "Got it" for <paramref name="beatId"/>.</summary>
    /// <returns>Whether the session accepted it (false when the beat closed itself first).</returns>
    bool Dismiss(string beatId);

    /// <summary>Submits guided first run on or off.</summary>
    bool SetEnabled(bool enabled);
}
