namespace Usa.Game.Flow;

/// <summary>
/// Builds and tears down mission sessions for the router. Implemented by the hosting layer; the router never
/// touches a session directly. The host answers <see cref="Start"/> and <see cref="Restart"/> with
/// <see cref="ScreenRouter.LoadingCompleted"/> or <see cref="ScreenRouter.LoadingFailed"/>.
/// </summary>
/// <remarks>
/// Contract:
/// <list type="bullet">
/// <item>The router enters <see cref="Screen.Loading"/> (and announces it) <em>before</em> it calls
/// <see cref="Start"/> or <see cref="Restart"/>, so the answer may be given re-entrantly, from inside the call, or on
/// any later frame.</item>
/// <item>At most one session exists. <see cref="Start"/> and <see cref="Restart"/> always tear down any session the
/// launcher still holds before building the new one; neither is ever rejected for a running session.</item>
/// <item>Every session the router saw start is ended by exactly one of <see cref="Restart"/>, <see cref="Abandon"/>
/// or <see cref="Complete"/>. A launch the host answered with <c>LoadingFailed</c> built no session and gets none.</item>
/// <item>All calls are on the main thread; teardown calls are idempotent.</item>
/// </list>
/// </remarks>
public interface IMissionLauncher
{
    void Start(MissionLaunch launch);

    /// <summary>Discards the running session without a grade and starts <paramref name="launch"/> (same mission, difficulty and seed).</summary>
    void Restart(MissionLaunch launch);

    /// <summary>Discards the running session without a grade (D10).</summary>
    void Abandon();

    /// <summary>The mission ended and its debrief is shown: stop driving the session and release it.</summary>
    void Complete();
}
