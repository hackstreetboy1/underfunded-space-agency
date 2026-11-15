namespace Usa.Game.Services.Backend;

/// <summary>
/// Whether the game may start the local proxy itself, and with which script. <see cref="AutoStart"/> is meant to come from
/// the "Start the local proxy automatically" setting (default on in development builds).
/// </summary>
/// <param name="ScriptPath">Absolute path of <c>backend/run_backend.ps1</c>; null when it was not found.</param>
public sealed record BackendLaunchOptions(bool AutoStart, string? ScriptPath)
{
    public const string ScriptRelativePath = "backend/run_backend.ps1";

    /// <summary>Overrides the default: <c>1</c>/<c>true</c> or <c>0</c>/<c>false</c>.</summary>
    public const string AutoStartVariable = "USA_BACKEND_AUTOSTART";

    /// <summary>The address <c>run_backend.ps1</c> listens on.</summary>
    public const int ScriptPort = 8787;

    public bool CanStart => AutoStart && ScriptPath is not null;

    /// <summary>Starting the script helps only when the client talks to the address it serves.</summary>
    public static bool ServesAddress(Uri backendUrl)
    {
        ArgumentNullException.ThrowIfNull(backendUrl);
        return backendUrl.IsLoopback && backendUrl.Port == ScriptPort;
    }

    /// <summary>The first candidate that exists (project folder when run from the editor, then next to the executable).</summary>
    public static string? FindScript(IEnumerable<string?> candidates, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(exists);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The build default unless <see cref="AutoStartVariable"/> says otherwise; anything unrecognised keeps the default.</summary>
    public static bool ResolveAutoStart(bool buildDefault, string? variable) => variable?.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "on" or "yes" => true,
        "0" or "false" or "off" or "no" => false,
        _ => buildDefault,
    };
}
