namespace Usa.Game.Flow;

/// <summary>Full-screen states of the front-end flow (UI.md 5). Overlays (Esc menu, connection freeze, encyclopedia sheet) are tracked separately by <see cref="ScreenRouter"/>.</summary>
public enum Screen
{
    MainMenu,
    MissionSelect,
    Briefing,
    Loading,
    Mission,
    Debrief,
    Settings,
    Credits,

    /// <summary>The full-screen encyclopedia reached from the main menu (the in-mission side sheet is <see cref="ScreenRouter.EncyclopediaSheetOpen"/>).</summary>
    Encyclopedia,
}
