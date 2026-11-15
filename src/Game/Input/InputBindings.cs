using Godot;

namespace Usa.Game.Input;

/// <summary>
/// Input actions and their default bindings (UI.md 6.1). Registered in code so the settings
/// screen can rebind them and persist the result without touching project.godot.
/// </summary>
public static class InputBindings
{
    public static readonly StringName PushToTalk = "ptt";
    public static readonly StringName FocusComms = "focus_comms";
    public static readonly StringName View1 = "view_1";
    public static readonly StringName View2 = "view_2";
    public static readonly StringName View3 = "view_3";
    public static readonly StringName View4 = "view_4";
    public static readonly StringName AcknowledgeAlarms = "ack_alarms";
    public static readonly StringName Search = "search";
    public static readonly StringName FocusVehicle = "focus_vehicle";
    public static readonly StringName AdvanceEvent = "advance_event";
    public static readonly StringName Encyclopedia = "encyclopedia";
    public static readonly StringName Menu = "menu";
    public static readonly StringName DebugOverlay = "debug_overlay";

    /// <summary>F11 or Alt+Enter: windowed / borderless fullscreen (not rebindable).</summary>
    public static readonly StringName ToggleFullscreen = "toggle_fullscreen";

    public static void RegisterDefaults()
    {
        Bind(PushToTalk, Key(Godot.Key.Space), Mouse(MouseButton.Xbutton1));
        Bind(FocusComms, Key(Godot.Key.Enter), Key(Godot.Key.T));
        Bind(View1, Key(Godot.Key.Key1));
        Bind(View2, Key(Godot.Key.Key2));
        Bind(View3, Key(Godot.Key.Key3));
        Bind(View4, Key(Godot.Key.Key4));
        Bind(AcknowledgeAlarms, Key(Godot.Key.A, ctrl: true));
        Bind(Search, Key(Godot.Key.F, ctrl: true));
        Bind(FocusVehicle, Key(Godot.Key.F));
        Bind(AdvanceEvent, Key(Godot.Key.N));
        Bind(Encyclopedia, Key(Godot.Key.F1));
        Bind(Menu, Key(Godot.Key.Escape));
        Bind(DebugOverlay, Key(Godot.Key.F12));
        Bind(ToggleFullscreen, Key(Godot.Key.F11), Key(Godot.Key.Enter, alt: true), Key(Godot.Key.KpEnter, alt: true));
    }

    private static void Bind(StringName action, params InputEvent[] events)
    {
        if (InputMap.HasAction(action)) InputMap.EraseAction(action);
        InputMap.AddAction(action);
        foreach (var inputEvent in events) InputMap.ActionAddEvent(action, inputEvent);
    }

    private static InputEventKey Key(Key key, bool ctrl = false, bool alt = false) =>
        new() { PhysicalKeycode = key, CtrlPressed = ctrl, AltPressed = alt };

    private static InputEventMouseButton Mouse(MouseButton button) => new() { ButtonIndex = button };
}
