using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Snapshots;

namespace Usa.Game.Presentation.Console.Header;

/// <summary>The master alarm annunciator and header alarm counts (UI.md 3.1, 3.10).</summary>
/// <param name="CriticalBand">Show the full-width 8 px red band under the header.</param>
/// <param name="CautionCount">Active cautions, shown as the header count.</param>
public sealed record MasterAlarmIndicator(MasterAlarmState State, bool CriticalBand, int CautionCount, int Unacknowledged)
{
    public static MasterAlarmIndicator Off { get; } = new(MasterAlarmState.Off, false, 0, 0);

    public static MasterAlarmIndicator From(AlarmSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var state = summary.HighestUnacknowledged switch
        {
            AlarmPriority.Master => MasterAlarmState.Flashing,
            AlarmPriority.Warning => MasterAlarmState.Lit,
            _ => MasterAlarmState.Off,
        };
        return new MasterAlarmIndicator(state, state == MasterAlarmState.Flashing, summary.ActiveCautions, summary.Unacknowledged);
    }
}
