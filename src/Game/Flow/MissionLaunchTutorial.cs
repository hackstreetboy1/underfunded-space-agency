using Usa.Game.Flow.Tutorial;

namespace Usa.Game.Flow;

/// <summary>Turns a launch's "Guided first run" choice into the new session's tutorial input.</summary>
public static class MissionLaunchTutorial
{
    /// <summary>
    /// Call once a fresh launch's session is built, before it advances. The briefing's choice (its default already folds in the
    /// difficulty, the profile and Settings) is submitted at MET start only when it differs from the difficulty default, so a
    /// default run logs nothing. A resume does nothing: its command log replays the choice.
    /// </summary>
    /// <returns>Whether a command was submitted and accepted.</returns>
    public static bool Apply(MissionLaunch launch, ITutorialSession session)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(session);
        if (launch.Resume is not null || session.IsEnabled == launch.GuidedFirstRun) return false;
        return session.SetEnabled(launch.GuidedFirstRun);
    }
}
