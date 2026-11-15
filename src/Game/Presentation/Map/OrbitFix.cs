using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// Where the ground's orbit comes from (GDD 4.1: every displayed number carries source, MET and state), shown with
/// the apsis labels.
/// </summary>
/// <param name="Source">e.g. <c>ground tracking</c> or <c>crew report</c>.</param>
/// <param name="At">MET of the fix the conic was fitted to.</param>
/// <param name="Estimated">The conic is a ground-model estimate (propagated past its fix or without tracking): labelled <c>EST</c>.</param>
/// <param name="StaleAfterS">
/// No fix for longer than this makes the orbit STALE (X8: <c>navigation.json</c> <c>staleAfterS</c>, 30 min); null never goes stale.
/// </param>
public sealed record OrbitFix(string Source, MissionTime At, bool Estimated = false, double? StaleAfterS = null)
{
    /// <summary>Seconds since the fix; negative for a fix in the future.</summary>
    public double AgeS(MissionTime now) => (now.Milliseconds - At.Milliseconds) / 1000.0;

    public bool IsStale(MissionTime now) => StaleAfterS is { } limit && AgeS(now) > limit;

    public OrbitFixState StateAt(MissionTime now) =>
        IsStale(now) ? OrbitFixState.Stale : Estimated ? OrbitFixState.Estimated : OrbitFixState.Known;
}

/// <summary>Knowledge state of the drawn orbit (UI 2.3).</summary>
public enum OrbitFixState
{
    /// <summary>The host gave no fix.</summary>
    None,
    Known,
    Estimated,
    Stale,
}
