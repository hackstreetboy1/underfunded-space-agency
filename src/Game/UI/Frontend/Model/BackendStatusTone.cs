using Usa.Game.Flow.Backend;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>Status language for the ground-link line (UI.md 2.2: shape and word, never colour alone).</summary>
public static class BackendStatusTone
{
    public static DebriefTone Of(BackendState state) => state switch
    {
        BackendState.Connected or BackendState.FakeMode => DebriefTone.Ok,
        BackendState.NotRunning or BackendState.ServiceProblem => DebriefTone.Critical,
        BackendState.Starting => DebriefTone.Caution,
        _ => DebriefTone.Neutral,
    };
}
