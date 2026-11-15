using Usa.Simulation.Session;

namespace Usa.Game.Guidance.Glue;

/// <summary><see cref="ITipGuidanceControl"/> over the running session's guidance director, at the session's current time.</summary>
public sealed class SessionTipGuidanceControl(MissionSession session) : ITipGuidanceControl
{
    public bool Dismiss() => session.Guidance.Dismiss(session.Now);

    public bool RequestStrongerHint() => session.Guidance.RequestStrongerHint(session.Now);
}
