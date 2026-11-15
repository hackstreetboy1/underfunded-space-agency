namespace Usa.Game.Diagnostics;

/// <summary>
/// The game's version. <c>project.godot</c> carries the same value as <c>application/config/version</c> (exports and
/// <c>ProjectSettings</c> read that one; a test keeps the two equal), so saves, logs and repros never say "dev".
/// </summary>
public static class AppVersion
{
    public const string Current = "0.1.0";

    public const string SettingPath = "application/config/version";
}
