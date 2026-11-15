namespace Usa.Game.Flow.Tutorial;

/// <summary>The UI anchors <c>tutorial.json</c> may point a callout at; the mission console exposes each one.</summary>
public static class TutorialAnchorIds
{
    /// <summary>The Godot group the <see cref="ITutorialAnchors"/> scene joins, so the callout layer can ask it for <see cref="ITutorialAnchors.Resolve"/>.</summary>
    public const string Group = "tutorial_anchors";

    /// <summary>The comms text input row (ask the crew for a reading).</summary>
    public const string CommsInput = "comms_input";

    /// <summary>The comms quick-command buttons (the input row when none are shown): where the first order is given.</summary>
    public const string CommsQuick = "comms_quick";

    /// <summary>The first "Ask" button of an unknown readout on the systems board.</summary>
    public const string ReadoutAsk = "readout_ask";

    /// <summary>The source and timestamp of a readout (who read it, when); the first visible readout's stamp.</summary>
    public const string FactSourceStamp = "fact_source_stamp";

    /// <summary>The crew task bar (one task at a time).</summary>
    public const string TaskBar = "task_bar";

    /// <summary>The resource strip with its limiting-supply forecast.</summary>
    public const string ResourceStrip = "resource_strip";

    /// <summary>The power section of the systems board (loads, watts, dependencies).</summary>
    public const string SystemsPower = "systems_power";

    /// <summary>The orbit map view (its tab button while another main view is shown).</summary>
    public const string OrbitMap = "orbit_map";

    /// <summary>The ACK button of the first unacknowledged alarm.</summary>
    public const string AlarmAck = "alarm_ack";

    /// <summary>The burn planner's engine dropdown (first the Orbit tab, then the planner button, while hidden).</summary>
    public const string PlannerEngine = "planner_engine";

    /// <summary>The burn planner's proposal goal picker ("Zone: Primary Atlantic"; Easy), reached like the engine dropdown.</summary>
    public const string PlannerGoal = "planner_goal";

    /// <summary>The burn planner's Propose button (Easy).</summary>
    public const string PlannerPropose = "planner_propose";

    /// <summary>The burn planner's Execute button.</summary>
    public const string PlannerExecute = "planner_execute";

    /// <summary>The Confirm button of the open confirmation dialog (high-impact orders, the planner's Execute).</summary>
    public const string ConfirmDialog = "confirm_dialog";

    /// <summary>The splashdown / recovery card.</summary>
    public const string RecoveryCard = "recovery_card";

    /// <summary>"Proceed to debrief": the recovery card's button once the crew is recovered, else the closing card's Proceed.</summary>
    public const string ProceedDebrief = "proceed_debrief";

    public static IReadOnlyList<string> All { get; } =
    [
        CommsInput, CommsQuick, ReadoutAsk, FactSourceStamp, TaskBar, ResourceStrip, SystemsPower, OrbitMap,
        AlarmAck, PlannerEngine, PlannerGoal, PlannerPropose, PlannerExecute, ConfirmDialog, RecoveryCard, ProceedDebrief,
    ];

    /// <summary>
    /// Anchors that are a single control: pressing the highlighted control itself completes its beat (a dismiss through the
    /// command log). Anchors whose action needs more than a press (typing an order) close through the beat's completion condition.
    /// </summary>
    public static IReadOnlyList<string> CompletesOnPress { get; } =
        [AlarmAck, PlannerEngine, PlannerGoal, PlannerPropose, PlannerExecute, ConfirmDialog, ProceedDebrief];

    /// <summary>
    /// Anchors among <see cref="CompletesOnPress"/> whose press alone proves nothing: opening a dropdown, or a dialog button that
    /// may also be answered with Enter. Their beat completes when <see cref="ITutorialAnchors.StateOf"/> changes (an item
    /// chosen, the burn order held).
    /// </summary>
    public static IReadOnlyList<string> CompletesOnChange { get; } = [PlannerEngine, PlannerGoal, ConfirmDialog];

    public static bool IsKnown(string anchorId) => All.Contains(anchorId, StringComparer.Ordinal);
}
