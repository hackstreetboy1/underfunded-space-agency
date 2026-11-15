namespace Usa.Simulation.Guidance;

/// <summary>The console controls the rails gate (<see cref="ITutorialRails.IsAllowed"/>); an item follows a colon (<c>planner_engine:retro_sequence</c>).</summary>
public static class TutorialControls
{
    public const string ViewTab = "view_tab";
    public const string Menu = "menu";
    public const string Advance = "advance";
    public const string CommsInput = "comms_input";
    public const string CommsQuick = "comms_quick";
    public const string TranscriptOffer = "transcript_offer";
    public const string ReadoutAsk = "readout_ask";
    public const string AlarmAck = "alarm_ack";
    public const string SystemsSwitch = "systems_switch";
    public const string TaskStop = "task_stop";
    public const string TipCard = "tip_card";
    public const string Workspace = "workspace";
    public const string PlannerToggle = "planner_toggle";
    public const string PlannerEngine = "planner_engine";
    public const string PlannerGoal = "planner_goal";
    public const string PlannerPropose = "planner_propose";
    public const string PlannerExecute = "planner_execute";
    public const string PlannerEdit = "planner_edit";
    public const string PlannerClear = "planner_clear";
    public const string MapDrag = "map_drag";
    public const string ProceedDebrief = "proceed_debrief";

    public static string Item(string control, string item) => control + ":" + item;
}
