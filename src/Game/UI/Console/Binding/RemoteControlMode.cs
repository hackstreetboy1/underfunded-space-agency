using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Systems;

namespace Usa.Game.UI.Console.Binding;

/// <summary>Which affordance a systems-board control shows (UI.md 3.9).</summary>
public enum RemoteControlMode
{
    /// <summary>Remote two-position switch: a toggle with its position word.</summary>
    Toggle,

    /// <summary>Remote momentary action: a button labelled with the action.</summary>
    Action,

    /// <summary>Remote critical load: the hinged cover, then the switch, then the confirmation.</summary>
    Guarded,

    /// <summary>Cockpit or physical work: greyed, with "Request…" that pre-fills a crew order.</summary>
    CrewRequest,
}

public static class RemoteControlModes
{
    public static RemoteControlMode Of(bool crewOnly, GuardState guard, ControlKind kind)
    {
        if (crewOnly) return RemoteControlMode.CrewRequest;
        if (guard != GuardState.NotGuarded) return RemoteControlMode.Guarded;
        return kind == ControlKind.Button ? RemoteControlMode.Action : RemoteControlMode.Toggle;
    }
}
