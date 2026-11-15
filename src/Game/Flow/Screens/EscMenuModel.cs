using Usa.Game.Content;

namespace Usa.Game.Flow.Screens;

/// <summary>
/// Esc menu (UI.md 5, D12): a full-screen cover over the frozen mission with Resume, Settings, Restart mission
/// (confirm) and Abandon to menu (confirm, no grade). There is deliberately no encyclopedia entry.
/// </summary>
public sealed class EscMenuModel
{
    private readonly ScreenRouter _router;
    private readonly StringTable _strings;

    public EscMenuModel(ScreenRouter router, StringTable strings)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(strings);
        _router = router;
        _strings = strings;
        Title = strings.Get("escMenu.title");
        Body = strings.Get("escMenu.body");
        ResumeLabel = strings.Get("escMenu.resume");
        SettingsLabel = strings.Get("escMenu.settings");
        RestartLabel = strings.Get("escMenu.restart");
        AbandonLabel = strings.Get("escMenu.abandon");
        ConfirmLabel = strings.Get("escMenu.confirm");
        CancelLabel = strings.Get("escMenu.cancel");
        EncyclopediaUnavailable = strings.Get("escMenu.encyclopediaUnavailable");
    }

    /// <summary>The cover is opaque (<c>bg.0</c> at 100 %) so a frozen mission can't be studied.</summary>
    public const bool CoversScreen = true;

    public bool Visible => _router.EscMenuVisible;

    public EscMenuState State => _router.EscMenu;

    /// <summary>"Simulation frozen".</summary>
    public string Title { get; }

    public string Body { get; }

    public string ResumeLabel { get; }

    public string SettingsLabel { get; }

    public string RestartLabel { get; }

    public string AbandonLabel { get; }

    public string ConfirmLabel { get; }

    public string CancelLabel { get; }

    /// <summary>Shown if the player presses the encyclopedia key while frozen.</summary>
    public string EncyclopediaUnavailable { get; }

    /// <summary>The confirmation question while a restart or abandon awaits confirmation, else null.</summary>
    public string? ConfirmPrompt => _router.EscMenu switch
    {
        EscMenuState.ConfirmRestart => _strings.Get("escMenu.restartConfirm"),
        EscMenuState.ConfirmAbandon => _strings.Get("escMenu.abandonConfirm"),
        _ => null,
    };

    public bool Resume() => _router.ResumeMission();

    public bool OpenSettings() => _router.OpenSettings();

    public bool Restart() => _router.RequestRestart();

    public bool Abandon() => _router.RequestAbandon();

    public bool Confirm() => _router.ConfirmEscAction();

    public bool Cancel() => _router.CancelConfirmation();
}
