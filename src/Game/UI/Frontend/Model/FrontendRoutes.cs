using Usa.Game.Flow;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>What the front-end shell shows for a router state: one full-screen scene, the mission underneath, and the overlays.</summary>
public static class FrontendRoutes
{
    public const string MainMenu = "res://ui/screens/MainMenu.tscn";
    public const string MissionSelect = "res://ui/screens/MissionSelect.tscn";
    public const string Briefing = "res://ui/screens/Briefing.tscn";
    public const string Loading = "res://ui/screens/Loading.tscn";
    public const string Debrief = "res://ui/screens/Debrief.tscn";
    public const string Settings = "res://ui/screens/Settings.tscn";
    public const string Credits = "res://ui/screens/Credits.tscn";
    public const string Encyclopedia = "res://ui/screens/Encyclopedia.tscn";
    public const string EscMenu = "res://ui/overlays/EscMenu.tscn";
    public const string EncyclopediaSheet = "res://ui/overlays/EncyclopediaSheet.tscn";
    public const string TutorialCallout = "res://ui/overlays/TutorialCallout.tscn";
    public const string ConnectionOverlay = "res://ui/overlays/ConnectionOverlay.tscn";

    /// <summary>The console engineer's mission scene; the shell shows a plain placeholder until it exists.</summary>
    public const string MissionConsole = "res://ui/console/MissionConsole.tscn";

    /// <summary>The full-screen scene for <paramref name="screen"/>; null for <see cref="Screen.Mission"/> (the console).</summary>
    public static string? SceneFor(Screen screen) => screen switch
    {
        Screen.MainMenu => MainMenu,
        Screen.MissionSelect => MissionSelect,
        Screen.Briefing => Briefing,
        Screen.Loading => Loading,
        Screen.Debrief => Debrief,
        Screen.Settings => Settings,
        Screen.Credits => Credits,
        Screen.Encyclopedia => Encyclopedia,
        _ => null,
    };

    /// <summary>
    /// The mission scene stays alive (frozen, hidden) behind Settings opened from the Esc menu, and from the moment the
    /// session is composed until the run ends.
    /// </summary>
    public static bool KeepsMissionScene(Screen screen, Screen settingsReturn) =>
        screen == Screen.Mission || (screen == Screen.Settings && settingsReturn == Screen.Mission);

    public static bool ShowsMissionScene(Screen screen) => screen == Screen.Mission;
}
