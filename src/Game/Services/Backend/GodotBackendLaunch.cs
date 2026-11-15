using System.IO;
using Godot;
using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Backend;

/// <summary>Resolves <see cref="BackendLaunchOptions"/> and builds the launcher for the running build.</summary>
public static class GodotBackendLaunch
{
    /// <param name="backendUrl">The client's proxy address; the script serves only the default one, so any other address turns auto-start off.</param>
    /// <param name="autoStartSetting">The player's setting once the settings carry one; null uses the build default (on in debug builds).</param>
    public static BackendLaunchOptions ResolveOptions(Uri backendUrl, bool? autoStartSetting = null)
    {
        ArgumentNullException.ThrowIfNull(backendUrl);
        var autoStart = (autoStartSetting
            ?? BackendLaunchOptions.ResolveAutoStart(OS.IsDebugBuild(), System.Environment.GetEnvironmentVariable(BackendLaunchOptions.AutoStartVariable)))
            && BackendLaunchOptions.ServesAddress(backendUrl);
        string?[] candidates =
        [
            ProjectSettings.GlobalizePath("res://" + BackendLaunchOptions.ScriptRelativePath),
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", BackendLaunchOptions.ScriptRelativePath),
        ];
        return new BackendLaunchOptions(autoStart, BackendLaunchOptions.FindScript(candidates, path => Path.IsPathRooted(path) && File.Exists(path)));
    }

    public static BackendLauncher CreateLauncher(BackendLaunchOptions options, Log log) =>
        new(options, new GodotProcessRunner(), log, GodotProcessRunner.IsWindows ? "powershell.exe" : "pwsh");
}
