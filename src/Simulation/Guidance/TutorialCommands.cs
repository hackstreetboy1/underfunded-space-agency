namespace Usa.Simulation.Guidance;

/// <summary>
/// Player input to the guided first run. The tutorial state is part of the state hash, so these go through the session's
/// command log like every other input and a replay repeats them; they are session commands, not catalog actions, so the AI is
/// never offered them and the crew never acknowledges them.
/// </summary>
public static class TutorialCommands
{
    /// <summary>"Got it" on a callout; argument <see cref="BeatArg"/> (text). A beat that is no longer on screen is left alone.</summary>
    public const string DismissBeat = "dismiss_tutorial_beat";

    /// <summary>Guided first run on or off (briefing toggle at launch, Settings mid-run); argument <see cref="EnabledArg"/> (boolean).</summary>
    public const string SetEnabled = "set_tutorial_enabled";

    public const string BeatArg = "beat";

    public const string EnabledArg = "enabled";

    /// <summary>Refusal: the dismissed beat is not the callout on screen (it closed itself in the meantime).</summary>
    public const string NotShownReason = "tutorial_beat_not_shown";

    public static bool IsTutorialCommand(string actionId) => actionId is DismissBeat or SetEnabled;
}
