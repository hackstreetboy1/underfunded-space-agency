namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>Event-log row categories (UI.md 3.8).</summary>
public enum LogCategory
{
    Alarm,
    Crew,
    Command,
    SystemChange,
    Decision,

    /// <summary>Everything else; listed only under "All".</summary>
    Info,
}
