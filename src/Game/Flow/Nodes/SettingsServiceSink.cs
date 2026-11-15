using Usa.Game.Flow.Screens;
using Usa.Game.Services.Settings;

namespace Usa.Game.Flow.Nodes;

/// <summary><see cref="ISettingsSink"/> over the game's <see cref="SettingsService"/>.</summary>
public sealed class SettingsServiceSink(SettingsService settings) : ISettingsSink
{
    public GameSettings Current => settings.Current;

    public bool Update(GameSettings next) => settings.Update(next);

    public bool Replace(GameSettings next) => settings.Replace(next);
}
