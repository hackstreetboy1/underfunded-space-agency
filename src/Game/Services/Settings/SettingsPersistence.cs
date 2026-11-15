namespace Usa.Game.Services.Settings;

/// <summary>Whether <see cref="SettingsService"/> may write <c>settings.json</c>.</summary>
public enum SettingsPersistence
{
    Writable,

    /// <summary>
    /// The file exists but could not be read at start-up. Edits apply for this session only until a later read
    /// succeeds (each edit retries it) or the player resets to defaults.
    /// </summary>
    ReadFailed,

    /// <summary>The file was written by a newer build. Edits apply for this session only; this build never overwrites it.</summary>
    NewerFile,
}
