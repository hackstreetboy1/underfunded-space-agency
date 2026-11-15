namespace Usa.Game.Presentation.CrewFeed;

/// <summary>What the crew member's single task slot is doing, as the feed shows it.</summary>
public enum CrewActivity
{
    None,
    Operate,
    Inspection,
    Test,
    Repair,
    Eva,
    Procedure,

    /// <summary>A glance read (reading a gauge on request), not a crew task.</summary>
    GlanceRead,
}
