using Usa.Game.Content;
using Usa.Game.Services.Saves;
using Usa.Simulation.Kernel;

namespace Usa.Game.Flow;

/// <summary>
/// The front-end state machine (UI.md 5, GDD 3.2): MainMenu → MissionSelect → Briefing → Loading → Mission →
/// Debrief, with Settings, Credits and the encyclopedia off the main menu. During a mission it owns the Esc menu
/// (freezes with <see cref="FreezeReason.Menu"/> and fully covers; the encyclopedia is not reachable from it) and the
/// connection-freeze overlay (<see cref="FreezeReason.ServiceOutage"/>), which are independent of each other.
/// Every action returns false and raises <see cref="Rejected"/> when the current state does not allow it.
/// Main thread only.
/// </summary>
public sealed class ScreenRouter
{
    private readonly IMissionLauncher _launcher;
    private readonly IFreezeControl _freeze;
    private Screen _settingsReturn = Screen.MainMenu;
    private long _runNumber;
    private ulong? _runningSeed;
    private bool _menuFrozen;
    private bool _outageFrozen;
    private bool _loadingFrozen;

    public ScreenRouter(IMissionLauncher launcher, IFreezeControl freeze)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(freeze);
        _launcher = launcher;
        _freeze = freeze;
    }

    /// <summary>Raised after the full-screen state changed (before <see cref="Changed"/>).</summary>
    public event Action<ScreenChange>? ScreenChanged;

    /// <summary>Raised after any state change, overlays included.</summary>
    public event Action? Changed;

    public event Action<FlowRejection>? Rejected;

    /// <summary>The player chose Quit on the main menu.</summary>
    public event Action? QuitRequested;

    public Screen Current { get; private set; } = Screen.MainMenu;

    /// <summary>Where Back from Settings goes: the main menu, or the mission (Esc menu still open).</summary>
    public Screen SettingsReturn => _settingsReturn;

    public EscMenuState EscMenu { get; private set; } = EscMenuState.Closed;

    /// <summary>The 760-wide encyclopedia side sheet during a mission; the simulation keeps running.</summary>
    public bool EncyclopediaSheetOpen { get; private set; }

    /// <summary>A mission-console dialog (e.g. a high-impact confirmation) is up; Esc closes it, never the menu (UI.md 5.2).</summary>
    public bool MissionDialogOpen { get; private set; }

    /// <summary>The connection monitor reports the essential services as lost (driven by events, any screen).</summary>
    public bool ConnectionLost { get; private set; }

    public MissionSelection? Selection { get; private set; }

    /// <summary>The launch being loaded or flown; null outside a mission.</summary>
    public MissionLaunch? CurrentLaunch { get; private set; }

    /// <summary>The run shown on the debrief.</summary>
    public CompletedMission? LastCompleted { get; private set; }

    /// <summary>The flight that last ended without a debrief, for the main menu's notice; cleared once the player leaves the menu.</summary>
    public MissionAbort? LastAborted { get; private set; }

    /// <summary>A session is loading or flying, including while Settings is open from the Esc menu.</summary>
    public bool MissionInProgress => CurrentLaunch is not null
        && (Current is Screen.Loading or Screen.Mission || (Current == Screen.Settings && _settingsReturn == Screen.Mission));

    /// <summary>The connection overlay covers everything, the Esc menu included, and takes all input.</summary>
    public bool ConnectionOverlayVisible => ConnectionLost && MissionInProgress;

    public bool EscMenuVisible => Current == Screen.Mission && EscMenu != EscMenuState.Closed && !ConnectionOverlayVisible;

    public bool OpenMissionSelect()
    {
        if (Current is not (Screen.MainMenu or Screen.Debrief)) return Reject(nameof(OpenMissionSelect), "Mission select opens from the main menu or the debrief.");
        if (Current == Screen.Debrief) LastCompleted = null;
        return GoTo(Screen.MissionSelect);
    }

    /// <summary>Continue from a resume save (main menu only).</summary>
    public bool Continue(ResumeSave resume)
    {
        ArgumentNullException.ThrowIfNull(resume);
        if (Current != Screen.MainMenu) return Reject(nameof(Continue), "Continue is on the main menu.");
        var metadata = resume.Metadata;
        Selection = null;
        Launch(metadata.MissionId, metadata.Difficulty, seed: null, guidedFirstRun: false, resume);
        return true;
    }

    public bool OpenSettings()
    {
        if (Current == Screen.MainMenu)
        {
            _settingsReturn = Screen.MainMenu;
            return GoTo(Screen.Settings);
        }

        if (Current == Screen.Mission && EscMenu == EscMenuState.Open && !ConnectionOverlayVisible)
        {
            _settingsReturn = Screen.Mission;
            return GoTo(Screen.Settings);
        }
        return Reject(nameof(OpenSettings), "Settings opens from the main menu or the open Esc menu.");
    }

    public bool OpenCredits() =>
        Current == Screen.MainMenu ? GoTo(Screen.Credits) : Reject(nameof(OpenCredits), "Credits open from the main menu.");

    /// <summary>The full-screen encyclopedia; never from the Esc menu (it would allow frozen analysis).</summary>
    public bool OpenEncyclopedia() =>
        Current == Screen.MainMenu ? GoTo(Screen.Encyclopedia) : Reject(nameof(OpenEncyclopedia), "The encyclopedia opens from the main menu, or as a sheet while the mission runs.");

    public bool Quit()
    {
        if (Current != Screen.MainMenu) return Reject(nameof(Quit), "Quit is on the main menu.");
        QuitRequested?.Invoke();
        return true;
    }

    /// <summary>Back out of a menu screen: Settings, Credits, Encyclopedia, Mission select and Briefing.</summary>
    public bool Back()
    {
        switch (Current)
        {
            case Screen.MissionSelect:
            case Screen.Credits:
            case Screen.Encyclopedia:
                return GoTo(Screen.MainMenu);
            case Screen.Briefing:
                return GoTo(Screen.MissionSelect);
            case Screen.Settings when _settingsReturn == Screen.Mission:
                if (ConnectionOverlayVisible) return Reject(nameof(Back), "The connection overlay is up.");
                return GoTo(Screen.Mission);
            case Screen.Settings:
                return GoTo(Screen.MainMenu);
            default:
                return Reject(nameof(Back), "There is nothing to go back to from here.");
        }
    }

    public bool SelectMission(MissionSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (Current != Screen.MissionSelect) return Reject(nameof(SelectMission), "Missions are chosen on mission select.");
        Selection = selection;
        return GoTo(Screen.Briefing);
    }

    public bool StartMission(bool guidedFirstRun)
    {
        if (Current != Screen.Briefing || Selection is null) return Reject(nameof(StartMission), "A mission starts from its briefing.");
        Launch(Selection.MissionId, Selection.Difficulty, Selection.Seed, guidedFirstRun, resume: null);
        return true;
    }

    /// <summary>The host built the session; <paramref name="seed"/> is the seed it runs with (read from the payload when resuming).</summary>
    public bool LoadingCompleted(ulong seed)
    {
        if (Current != Screen.Loading || CurrentLaunch is null) return Reject(nameof(LoadingCompleted), "Nothing is loading.");
        _runningSeed = seed;
        SetLoadingFrozen(false);
        return GoTo(Screen.Mission);
    }

    /// <summary>The session could not be built; back to the briefing (or the main menu for a resume).</summary>
    public bool LoadingFailed()
    {
        if (Current != Screen.Loading || CurrentLaunch is null) return Reject(nameof(LoadingFailed), "Nothing is loading.");
        var wasResume = CurrentLaunch.Resume is not null;
        EndMissionContext();
        return GoTo(wasResume || Selection is null ? Screen.MainMenu : Screen.Briefing);
    }

    /// <summary>
    /// Esc key (UI.md 6.1): cancels an Esc-menu confirmation, else resumes from the Esc menu, else closes the
    /// encyclopedia sheet, else does nothing while a console dialog is up, else opens the Esc menu. On menu screens
    /// it goes back. Ignored while the connection overlay covers the screen.
    /// </summary>
    public bool HandleEscape()
    {
        switch (Current)
        {
            case Screen.Mission:
                if (ConnectionOverlayVisible) return false;
                if (EscMenu is EscMenuState.ConfirmRestart or EscMenuState.ConfirmAbandon) return CancelConfirmation();
                if (EscMenu == EscMenuState.Open) return ResumeMission();
                if (EncyclopediaSheetOpen) return CloseEncyclopediaSheet();
                if (MissionDialogOpen) return false;
                return OpenEscMenu();
            case Screen.MissionSelect:
            case Screen.Briefing:
            case Screen.Settings:
            case Screen.Credits:
            case Screen.Encyclopedia:
                return Back();
            default:
                return false;
        }
    }

    public bool OpenEscMenu()
    {
        if (Current != Screen.Mission || EscMenu != EscMenuState.Closed) return Reject(nameof(OpenEscMenu), "The Esc menu opens during a mission.");
        if (ConnectionOverlayVisible) return Reject(nameof(OpenEscMenu), "The connection overlay is up.");
        if (MissionDialogOpen) return Reject(nameof(OpenEscMenu), "Esc closes the open dialog first.");

        // The menu covers the whole screen, so nothing from the console (the sheet included) stays reachable.
        EncyclopediaSheetOpen = false;
        SetMenuFrozen(true);
        EscMenu = EscMenuState.Open;
        return Notify();
    }

    public bool ResumeMission()
    {
        if (!EscMenuAction(nameof(ResumeMission), EscMenuState.Open)) return false;
        EscMenu = EscMenuState.Closed;
        SetMenuFrozen(false);
        return Notify();
    }

    public bool RequestRestart()
    {
        if (!EscMenuAction(nameof(RequestRestart), EscMenuState.Open)) return false;
        EscMenu = EscMenuState.ConfirmRestart;
        return Notify();
    }

    public bool RequestAbandon()
    {
        if (!EscMenuAction(nameof(RequestAbandon), EscMenuState.Open)) return false;
        EscMenu = EscMenuState.ConfirmAbandon;
        return Notify();
    }

    public bool CancelConfirmation()
    {
        if (Current != Screen.Mission || EscMenu is not (EscMenuState.ConfirmRestart or EscMenuState.ConfirmAbandon))
            return Reject(nameof(CancelConfirmation), "No confirmation is open.");
        if (ConnectionOverlayVisible) return Reject(nameof(CancelConfirmation), "The connection overlay is up.");
        EscMenu = EscMenuState.Open;
        return Notify();
    }

    /// <summary>Confirms the pending restart (→ Loading, same seed) or abandon (→ main menu); neither is graded.</summary>
    public bool ConfirmEscAction()
    {
        if (Current != Screen.Mission || EscMenu is not (EscMenuState.ConfirmRestart or EscMenuState.ConfirmAbandon))
            return Reject(nameof(ConfirmEscAction), "No confirmation is open.");
        if (ConnectionOverlayVisible) return Reject(nameof(ConfirmEscAction), "The connection overlay is up.");

        var launch = CurrentLaunch!;
        if (EscMenu == EscMenuState.ConfirmRestart)
        {
            var restart = NextLaunch(launch.MissionId, launch.Difficulty, _runningSeed ?? launch.Seed, launch.GuidedFirstRun, resume: null);
            EscMenu = EscMenuState.Closed;
            EncyclopediaSheetOpen = false;
            MissionDialogOpen = false;
            CurrentLaunch = restart;
            _runningSeed = restart.Seed;
            SetLoadingFrozen(true);
            SetMenuFrozen(false);
            GoTo(Screen.Loading);

            // Last: the launcher may answer LoadingCompleted/LoadingFailed from inside this call.
            _launcher.Restart(restart);
            return true;
        }

        _launcher.Abandon();
        EndMissionContext();
        return GoTo(Screen.MainMenu);
    }

    public bool OpenEncyclopediaSheet()
    {
        if (Current != Screen.Mission || EscMenu != EscMenuState.Closed)
            return Reject(nameof(OpenEncyclopediaSheet), "The encyclopedia sheet opens while the mission runs, never from the Esc menu.");
        if (ConnectionOverlayVisible) return Reject(nameof(OpenEncyclopediaSheet), "The connection overlay is up.");
        if (EncyclopediaSheetOpen) return true;
        EncyclopediaSheetOpen = true;
        return Notify();
    }

    public bool CloseEncyclopediaSheet()
    {
        if (!EncyclopediaSheetOpen) return Reject(nameof(CloseEncyclopediaSheet), "The encyclopedia sheet is not open.");
        EncyclopediaSheetOpen = false;
        return Notify();
    }

    /// <summary>The mission console reports whether one of its dialogs is up.</summary>
    public void SetMissionDialogOpen(bool open)
    {
        var value = open && Current == Screen.Mission;
        if (MissionDialogOpen == value) return;
        MissionDialogOpen = value;
        Notify();
    }

    /// <summary>The mission ended by recovery or loss of the whole crew (D10); shows the debrief.</summary>
    public bool MissionEnded(DebriefView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (Current != Screen.Mission || CurrentLaunch is null || _runningSeed is null)
            return Reject(nameof(MissionEnded), "No mission is flying.");

        LastCompleted = new CompletedMission(CurrentLaunch, _runningSeed.Value, view);
        EndMissionContext();
        _launcher.Complete();
        return GoTo(Screen.Debrief);
    }

    /// <summary>
    /// The flight is over but no debrief can be shown (it failed to compose, or the router refused it): back to the main menu
    /// with <see cref="LastAborted"/> set, so the player is never left on a dead console. Not graded.
    /// </summary>
    public bool MissionAborted(MissionAbortReason reason)
    {
        if (!MissionInProgress || CurrentLaunch is null) return Reject(nameof(MissionAborted), "No mission is flying.");
        LastAborted = new MissionAbort(CurrentLaunch, reason);
        EndMissionContext();
        _launcher.Complete();
        return GoTo(Screen.MainMenu);
    }

    /// <summary>Same mission, difficulty and seed.</summary>
    public bool Replay() => Relaunch(nameof(Replay), SeedPlanner.Replay);

    /// <summary>Same mission and difficulty, the next seed of the retry sequence (a new variant).</summary>
    public bool Retry() => Relaunch(nameof(Retry), SeedPlanner.Retry);

    public void OnConnectionLost()
    {
        if (ConnectionLost) return;
        ConnectionLost = true;
        SyncOutageFreeze();
        Notify();
    }

    public void OnConnectionRestored()
    {
        if (!ConnectionLost) return;
        ConnectionLost = false;
        SyncOutageFreeze();
        Notify();
    }

    /// <remarks>
    /// The mission has now been flown, so the new run is not guided (GDD 9.4: on for first play); the player turns callouts back on
    /// in Settings, or picks the mission again from its briefing.
    /// </remarks>
    private bool Relaunch(string action, Func<ulong, ulong> nextSeed)
    {
        if (Current != Screen.Debrief || LastCompleted is null) return Reject(action, "Replay and Retry are on the debrief.");
        var completed = LastCompleted;
        var seed = nextSeed(completed.Seed);
        Selection = new MissionSelection(completed.Launch.MissionId, completed.Launch.Difficulty, seed);
        LastCompleted = null;
        Launch(completed.Launch.MissionId, completed.Launch.Difficulty, seed, guidedFirstRun: false, resume: null);
        return true;
    }

    private void Launch(string missionId, string difficulty, ulong? seed, bool guidedFirstRun, ResumeSave? resume)
    {
        var launch = NextLaunch(missionId, difficulty, seed, guidedFirstRun, resume);
        CurrentLaunch = launch;
        _runningSeed = seed;
        EscMenu = EscMenuState.Closed;
        EncyclopediaSheetOpen = false;
        MissionDialogOpen = false;
        LastAborted = null;
        var from = Current;
        Current = Screen.Loading;

        // Freeze reasons are set before the session exists, so it attaches already frozen.
        SetLoadingFrozen(true);
        SyncOutageFreeze();
        Announce(from);

        // Last: the launcher may answer LoadingCompleted/LoadingFailed from inside this call.
        _launcher.Start(launch);
    }

    private MissionLaunch NextLaunch(string missionId, string difficulty, ulong? seed, bool guidedFirstRun, ResumeSave? resume) =>
        new(++_runNumber, missionId, difficulty, seed, guidedFirstRun, resume);

    private void EndMissionContext()
    {
        EscMenu = EscMenuState.Closed;
        EncyclopediaSheetOpen = false;
        MissionDialogOpen = false;
        CurrentLaunch = null;
        _runningSeed = null;
        SetMenuFrozen(false);
        SetLoadingFrozen(false);
        SetOutageFrozen(false);
    }

    private bool EscMenuAction(string action, EscMenuState required)
    {
        if (Current != Screen.Mission || EscMenu != required) return Reject(action, "The Esc menu is not open.");
        if (ConnectionOverlayVisible) return Reject(action, "The connection overlay is up.");
        return true;
    }

    private void SyncOutageFreeze() => SetOutageFrozen(ConnectionLost && MissionInProgress);

    private void SetMenuFrozen(bool frozen) => SetFrozen(ref _menuFrozen, frozen, FreezeReason.Menu);

    private void SetOutageFrozen(bool frozen) => SetFrozen(ref _outageFrozen, frozen, FreezeReason.ServiceOutage);

    private void SetLoadingFrozen(bool frozen) => SetFrozen(ref _loadingFrozen, frozen, FreezeReason.Loading);

    private void SetFrozen(ref bool state, bool frozen, FreezeReason reason)
    {
        if (state == frozen) return;
        state = frozen;
        if (frozen) _freeze.Freeze(reason);
        else _freeze.Unfreeze(reason);
    }

    private bool GoTo(Screen next)
    {
        if (next != Screen.MainMenu) LastAborted = null;
        var from = Current;
        Current = next;
        SyncOutageFreeze();
        Announce(from);
        return true;
    }

    private void Announce(Screen from)
    {
        if (from != Current) ScreenChanged?.Invoke(new ScreenChange(from, Current));
        Changed?.Invoke();
    }

    private bool Notify()
    {
        Changed?.Invoke();
        return true;
    }

    private bool Reject(string action, string reason)
    {
        Rejected?.Invoke(new FlowRejection(action, Current, reason));
        return false;
    }
}
