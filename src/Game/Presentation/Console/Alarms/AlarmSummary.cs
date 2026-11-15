using Usa.Game.Presentation.Console.Snapshots;

namespace Usa.Game.Presentation.Console.Alarms;

/// <summary>Totals for the header (master alarm, caution count) and the systems-board tab badges.</summary>
/// <param name="ActiveCautions">Active cautions, the header count (UI.md 3.10).</param>
/// <param name="HighestUnacknowledged">Highest unacknowledged priority; null when everything is acknowledged.</param>
/// <param name="PerSystem">One entry per <see cref="SystemsTab"/>, in tab order.</param>
public sealed record AlarmSummary(
    int Active,
    int Unacknowledged,
    int ActiveCautions,
    AlarmPriority? HighestUnacknowledged,
    IReadOnlyList<SystemAlarmCount> PerSystem)
{
    public static AlarmSummary Empty { get; } = new(0, 0, 0, null, EmptyPerSystem());

    public SystemAlarmCount For(SystemsTab system) => PerSystem[(int)system];

    public bool Equals(AlarmSummary? other) =>
        other is not null
        && Active == other.Active
        && Unacknowledged == other.Unacknowledged
        && ActiveCautions == other.ActiveCautions
        && HighestUnacknowledged == other.HighestUnacknowledged
        && PerSystem.SequenceEqual(other.PerSystem);

    public override int GetHashCode() => HashCode.Combine(Active, Unacknowledged, ActiveCautions, HighestUnacknowledged);

    internal static SystemAlarmCount[] EmptyPerSystem()
    {
        var tabs = Enum.GetValues<SystemsTab>();
        var counts = new SystemAlarmCount[tabs.Length];
        for (var i = 0; i < tabs.Length; i++) counts[i] = new SystemAlarmCount(tabs[i], 0, 0, null);
        return counts;
    }
}
