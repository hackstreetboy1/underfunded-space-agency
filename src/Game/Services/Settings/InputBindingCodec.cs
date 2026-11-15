using Godot;

namespace Usa.Game.Services.Settings;

/// <summary>Converts between <see cref="BindingSpec"/> and Godot input events, for applying and for the rebind UI.</summary>
public static class InputBindingCodec
{
    public static InputEvent? ToEvent(BindingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        switch (spec.Device)
        {
            case BindingDevice.Key when Enum.TryParse<Key>(spec.Code, ignoreCase: false, out var key) && Enum.IsDefined(key) && key != Key.None:
                return new InputEventKey { PhysicalKeycode = key, CtrlPressed = spec.Ctrl, ShiftPressed = spec.Shift, AltPressed = spec.Alt };
            case BindingDevice.MouseButton when Enum.TryParse<MouseButton>(spec.Code, ignoreCase: false, out var button) && Enum.IsDefined(button) && button != MouseButton.None:
                return new InputEventMouseButton { ButtonIndex = button, CtrlPressed = spec.Ctrl, ShiftPressed = spec.Shift, AltPressed = spec.Alt };
            default:
                return null;
        }
    }

    /// <summary>A spec for a key or mouse-button event (e.g. the one captured by a rebind prompt); null for other devices.</summary>
    public static BindingSpec? FromEvent(InputEvent inputEvent) => inputEvent switch
    {
        InputEventKey key when (key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode) is var code && code != Key.None =>
            new BindingSpec(BindingDevice.Key, code.ToString(), key.CtrlPressed, key.ShiftPressed, key.AltPressed),
        InputEventMouseButton mouse when mouse.ButtonIndex != MouseButton.None =>
            new BindingSpec(BindingDevice.MouseButton, mouse.ButtonIndex.ToString(), mouse.CtrlPressed, mouse.ShiftPressed, mouse.AltPressed),
        _ => null,
    };

    /// <summary>The action's current bindings as specs (defaults or overrides, whatever the InputMap holds now).</summary>
    public static IReadOnlyList<BindingSpec> Current(StringName action)
    {
        if (!InputMap.HasAction(action)) return [];
        var result = new List<BindingSpec>();
        foreach (var inputEvent in InputMap.ActionGetEvents(action))
        {
            if (FromEvent(inputEvent) is { } spec) result.Add(spec);
        }
        return result;
    }
}
