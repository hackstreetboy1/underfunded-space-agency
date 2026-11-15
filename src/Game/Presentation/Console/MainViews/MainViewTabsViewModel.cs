using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.MainViews;

/// <summary>
/// Which centre view is on screen (UI.md 3.6): Orbit, External, Diagram and, only while a repair workspace is open,
/// Workspace. Closing the workspace while it is shown returns to the Diagram. <see cref="ExternalVisible"/> follows the
/// selection, for the thruster pulse sounds that play only while the 3D view is on screen (GDD 14).
/// </summary>
public sealed class MainViewTabsViewModel : ViewModelBase
{
    private static readonly MainViewKind[] Views = Enum.GetValues<MainViewKind>();

    private readonly ConsoleWords _words;
    private readonly ObservableValue<MainViewKind> _selected = new(MainViewKind.Orbit);
    private readonly ObservableValue<bool> _workspaceAvailable = new();
    private readonly ObservableValue<bool> _externalVisible = new();
    private bool _workspaceOpenable;
    private readonly ObservableValue<IReadOnlyList<MainViewTab>> _tabs;

    public MainViewTabsViewModel(ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
        _tabs = new(BuildTabs(), ReferenceEqualityComparer.Instance);
    }

    public IReadOnlyObservable<MainViewKind> Selected => _selected;

    public IReadOnlyObservable<bool> WorkspaceAvailable => _workspaceAvailable;

    /// <summary>The External (3D) view is the selected view.</summary>
    public IReadOnlyObservable<bool> ExternalVisible => _externalVisible;

    /// <summary>
    /// Selecting the Workspace tab while no workspace is open, but one can be opened (<see cref="SetWorkspaceOpenable"/>): the
    /// console opens it and then shows it.
    /// </summary>
    public Signal<MainViewKind> WorkspaceRequested { get; } = new();

    /// <summary>The four tabs in order, with labels, key hints and enabled state.</summary>
    public IReadOnlyObservable<IReadOnlyList<MainViewTab>> Tabs => _tabs;

    /// <returns>False when the view cannot be shown now (Workspace without an open repair).</returns>
    public bool Select(MainViewKind view)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!Enum.IsDefined(view)) return false;
        if (view == MainViewKind.Workspace && !_workspaceAvailable.Value)
        {
            if (_workspaceOpenable) WorkspaceRequested.Raise(view);
            return false;
        }
        _selected.Value = view;
        _externalVisible.Value = view == MainViewKind.External;
        return true;
    }

    /// <summary>Keys <c>1</c>–<c>4</c>: index 0 is Orbit.</summary>
    public bool SelectIndex(int index) => index >= 0 && index < Views.Length && Select(Views[index]);

    /// <summary>A repair workspace opened (true) or closed (false).</summary>
    public void SetWorkspaceAvailable(bool available)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!_workspaceAvailable.Set(available)) return;
        _tabs.Value = BuildTabs();
        if (!available && _selected.Value == MainViewKind.Workspace) Select(MainViewKind.Diagram);
    }

    /// <summary>A repair workspace can be opened from the tab (a mission runtime is bound); the tab is enabled then too.</summary>
    public void SetWorkspaceOpenable(bool openable)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_workspaceOpenable == openable) return;
        _workspaceOpenable = openable;
        _tabs.Value = BuildTabs();
    }

    private MainViewTab[] BuildTabs()
    {
        var tabs = new MainViewTab[Views.Length];
        for (var i = 0; i < Views.Length; i++)
        {
            var view = Views[i];
            tabs[i] = new MainViewTab(
                view, _words.ViewLabel(view), (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                view != MainViewKind.Workspace || _workspaceAvailable.Value || _workspaceOpenable);
        }
        return tabs;
    }
}
