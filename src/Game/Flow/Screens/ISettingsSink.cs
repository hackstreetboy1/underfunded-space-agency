using Usa.Game.Services.Settings;

namespace Usa.Game.Flow.Screens;

/// <summary>Where applied settings go: <c>SettingsService</c> in the game (see <c>Nodes/SettingsServiceSink</c>), a fake in tests.</summary>
public interface ISettingsSink
{
    GameSettings Current { get; }

    /// <summary>Sanitises, applies and saves; false when saving failed (the settings still apply for this session).</summary>
    bool Update(GameSettings next);

    /// <summary>
    /// Like <see cref="Update"/>, but the player chose to replace everything (reset to defaults, then apply): a settings file
    /// that could not be read is overwritten too, instead of keeping its values where the reset left defaults.
    /// </summary>
    bool Replace(GameSettings next);
}
