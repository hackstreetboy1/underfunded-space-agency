using Usa.Game.Services.Settings;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>Key caps for the settings screen: "Space", "Mouse 4", "Ctrl+F", "Esc", "1".</summary>
public static class BindingText
{
    public static string Of(BindingSpec binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var name = binding.Device == BindingDevice.MouseButton ? MouseName(binding.Code) : KeyName(binding.Code);
        var prefix = (binding.Ctrl ? "Ctrl+" : "") + (binding.Alt ? "Alt+" : "") + (binding.Shift ? "Shift+" : "");
        return prefix + name;
    }

    /// <summary>The caps of every binding, joined for one row ("Space · Mouse 4").</summary>
    public static string Join(IReadOnlyList<BindingSpec> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return string.Join(" · ", bindings.Select(Of));
    }

    private static string KeyName(string code)
    {
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && char.IsAsciiDigit(code[3])) return code[3..];
        return code switch
        {
            "Escape" => "Esc",
            "Kp1" or "Kp2" or "Kp3" or "Kp4" or "Kp5" or "Kp6" or "Kp7" or "Kp8" or "Kp9" or "Kp0" => "Num " + code[2..],
            "Pageup" => "Page Up",
            "Pagedown" => "Page Down",
            "Quoteleft" => "`",
            _ => code,
        };
    }

    private static string MouseName(string code) => code switch
    {
        "Xbutton1" => "Mouse 4",
        "Xbutton2" => "Mouse 5",
        "Right" => "Right mouse",
        "Middle" => "Middle mouse",
        "Left" => "Left mouse",
        _ => "Mouse " + code,
    };
}
