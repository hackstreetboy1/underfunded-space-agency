using Usa.Game.Content;
using Usa.Game.Flow.Data;
using Usa.Game.Flow.Profile;
using Usa.Game.Services.Saves;

namespace Usa.Game.Flow.Screens;

public enum MainMenuAction
{
    Continue,
    Missions,
    Encyclopedia,
    Settings,
    Credits,
    Quit,
}

public sealed record MainMenuButton(MainMenuAction Action, string Label, bool Enabled);

/// <summary>
/// Main menu (UI.md 5): title lockup, APOD backdrop rotating per launch, and Continue / Missions / Encyclopedia /
/// Settings / Credits / Quit. Continue is enabled only while a valid resume file exists.
/// </summary>
public sealed class MainMenuModel
{
    private readonly ScreenRouter _router;
    private readonly IProfileStore _profiles;
    private readonly StringTable _strings;

    public MainMenuModel(ScreenRouter router, IProfileStore profiles, StringTable strings, NasaManifest manifest, int launchCount)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(manifest);
        _router = router;
        _profiles = profiles;
        _strings = strings;
        Title = strings.Get("agency.gameTitle");
        Subtitle = strings.Get("mainMenu.subtitle");
        Backdrop = MenuBackdrop.Pick(manifest, launchCount, strings);
        Buttons = [];
        Refresh();
    }

    public event Action? Changed;

    public string Title { get; }

    /// <summary>Tape-label subtitle, "est. on a shoestring".</summary>
    public string Subtitle { get; }

    /// <summary>Null when no main-menu imagery is cached (empty manifest).</summary>
    public MenuBackdrop? Backdrop { get; }

    /// <summary>The most recently saved resume file; Continue loads it.</summary>
    public ResumeMetadata? ContinueTarget { get; private set; }

    public bool ContinueEnabled => ContinueTarget is not null;

    public IReadOnlyList<MainMenuButton> Buttons { get; private set; }

    /// <summary>Re-reads the resume files (call when the menu is shown again).</summary>
    public void Refresh()
    {
        ResumeMetadata? latest = null;
        foreach (var resume in _profiles.ListResumes())
        {
            if (latest is null || resume.SavedUtc > latest.SavedUtc) latest = resume;
        }
        ContinueTarget = latest;
        Buttons =
        [
            new MainMenuButton(MainMenuAction.Continue, _strings.Get("mainMenu.continue"), ContinueEnabled),
            new MainMenuButton(MainMenuAction.Missions, _strings.Get("mainMenu.missions"), true),
            new MainMenuButton(MainMenuAction.Encyclopedia, _strings.Get("mainMenu.encyclopedia"), true),
            new MainMenuButton(MainMenuAction.Settings, _strings.Get("mainMenu.settings"), true),
            new MainMenuButton(MainMenuAction.Credits, _strings.Get("mainMenu.credits"), true),
            new MainMenuButton(MainMenuAction.Quit, _strings.Get("mainMenu.quit"), true),
        ];
        Changed?.Invoke();
    }

    public bool Activate(MainMenuAction action) => action switch
    {
        MainMenuAction.Continue => Continue(),
        MainMenuAction.Missions => _router.OpenMissionSelect(),
        MainMenuAction.Encyclopedia => _router.OpenEncyclopedia(),
        MainMenuAction.Settings => _router.OpenSettings(),
        MainMenuAction.Credits => _router.OpenCredits(),
        MainMenuAction.Quit => _router.Quit(),
        _ => false,
    };

    private bool Continue()
    {
        if (ContinueTarget is null) return false;
        var resume = _profiles.LoadResume(ContinueTarget.MissionId);
        if (resume is null)
        {
            // The file vanished or was damaged since the menu was shown.
            Refresh();
            return false;
        }
        return _router.Continue(resume);
    }
}
