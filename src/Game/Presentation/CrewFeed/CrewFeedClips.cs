namespace Usa.Game.Presentation.CrewFeed;

/// <summary>
/// The 12 <c>feed_*</c> clips in the crew GLBs (MODELING 4.5, gate-2 G2-04, 30 fps) and the AnimationTree
/// names the comm-feed scene is expected to use for them.
/// </summary>
public static class CrewFeedClips
{
    /// <summary>State-machine node and state names are the clip names without the <c>feed_</c> prefix.</summary>
    public static string StateName(CrewFeedState state) => state switch
    {
        CrewFeedState.Idle => "idle",
        CrewFeedState.Listen => "listen",
        CrewFeedState.Talk => "talk",
        CrewFeedState.Work => "work",
        CrewFeedState.ReadGauge => "read_gauge",
        CrewFeedState.Stressed => "stressed",
        CrewFeedState.SpinStrain => "spin_strain",
        CrewFeedState.Impaired => "impaired",
        CrewFeedState.GLoad => "g_load",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string OneShotName(CrewFeedOneShot oneShot) => oneShot switch
    {
        CrewFeedOneShot.Nod => "nod",
        CrewFeedOneShot.Wave => "wave",
        CrewFeedOneShot.Relief => "relief",
        _ => throw new ArgumentOutOfRangeException(nameof(oneShot), oneShot, null),
    };

    public static string ClipName(CrewFeedState state) => "feed_" + StateName(state);

    public static string ClipName(CrewFeedOneShot oneShot) => "feed_" + OneShotName(oneShot);

    /// <summary><c>feed_read_gauge</c> is authored as a one-pass clip; every other state clip loops.</summary>
    public static bool Loops(CrewFeedState state) => state != CrewFeedState.ReadGauge;

    public static double DurationSeconds(CrewFeedState state) => state switch
    {
        CrewFeedState.Idle => 8.0,
        CrewFeedState.Listen or CrewFeedState.Work or CrewFeedState.Impaired => 6.0,
        CrewFeedState.Talk or CrewFeedState.Stressed => 4.0,
        CrewFeedState.SpinStrain => 3.0,
        CrewFeedState.ReadGauge or CrewFeedState.GLoad => 2.0,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static double DurationSeconds(CrewFeedOneShot oneShot) => oneShot switch
    {
        CrewFeedOneShot.Nod => 0.8,
        CrewFeedOneShot.Wave or CrewFeedOneShot.Relief => 1.6,
        _ => throw new ArgumentOutOfRangeException(nameof(oneShot), oneShot, null),
    };
}
