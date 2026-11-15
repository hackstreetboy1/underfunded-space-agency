namespace Usa.Simulation.Session;

/// <summary>
/// Mission event ids the session raises from vehicle and flight events when a mission declares them (contract section 6). They
/// are a shared vocabulary of every mission's <c>mission.json</c>; staging events are named by each section's <c>jettison</c>.
/// </summary>
public static class MissionEventIds
{
    public const string Retrofire = "retrofire";
    public const string Drogue = "drogue";
    public const string Main = "main";
    public const string Splashdown = "splashdown";
    public const string Recovered = "recovered";

    /// <summary>Prefix of a mission event <c>source</c> raised when the named crew task succeeds, e.g. <c>task:jettison_retro</c>.</summary>
    public const string TaskSourcePrefix = "task:";
}
