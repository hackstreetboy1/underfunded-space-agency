using Godot;

namespace Usa.Game.Diagnostics.Overlay;

/// <summary>
/// Whether this launch gets the F12 debug overlay: debug builds only (editor and debug exports; release exports never), unless
/// switched off with <c>-- --no-debug-overlay</c> or the project setting <c>usa/debug/overlay_enabled=false</c>.
/// </summary>
public static class DebugOverlayGate
{
    public const string DisableArgument = "--no-debug-overlay";
    public const string SettingPath = "usa/debug/overlay_enabled";

    public static bool IsEnabled(out string reason)
    {
        if (!OS.IsDebugBuild())
        {
            reason = "release build";
            return false;
        }
        if (OS.GetCmdlineUserArgs().Contains(DisableArgument) || OS.GetCmdlineArgs().Contains(DisableArgument))
        {
            reason = DisableArgument;
            return false;
        }
        if (!ProjectSettings.GetSetting(SettingPath, true).AsBool())
        {
            reason = $"{SettingPath}=false";
            return false;
        }
        reason = "debug build";
        return true;
    }
}
