using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;

namespace Usa.Game.Flow.Tutorial;

/// <summary>
/// Connects one run's tutorial director to the callout layer (GDD 9.4, UI.md 5.1). Session events feed <see cref="Callouts"/>;
/// "Got it" there becomes a dismiss command; a change of the Settings "Tutorial callouts" option turns the guided run on or
/// off through the command log. A resumed run starts with the callout that was on screen when it was saved.
/// </summary>
public sealed class TutorialCalloutFeed : IDisposable
{
    private readonly ITutorialSession _session;
    private readonly ITutorialRails? _rails;
    private bool _setting;
    private string? _railsCallout;

    /// <param name="calloutsSetting">Settings "Tutorial callouts" when the run starts; only later changes of it act on the run.</param>
    /// <param name="rails">The run's tutorial rails: between two steps the guide says what the game is doing ("Wait — Cal is
    /// docking"), and a failed crew task brings the step back as "Try again" with the same send button.</param>
    public TutorialCalloutFeed(ITutorialSession session, bool calloutsSetting, ITutorialRails? rails = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _setting = calloutsSetting;
        Callouts = new TutorialCallouts(session.IsEnabled);
        if (session.Active is { } active) Callouts.Show(active);
        Callouts.Dismissed += OnDismissed;
        if (rails is null) return;
        _rails = rails;
        Callouts.AttachRails(rails);
        rails.Changed += SyncRails;
        SyncRails();
    }

    /// <summary>What <c>FrontendShell.AttachCallouts</c> binds.</summary>
    public TutorialCallouts Callouts { get; }

    /// <summary>Hand every drained session event here, in order; only the tutorial's own events are read.</summary>
    public void Observe(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case TutorialCalloutShown shown:
                Callouts.Show(new TutorialCallout(shown.BeatId, shown.Anchor, shown.Text, shown.Title, shown.Order));
                break;
            case TutorialCalloutClosed closed:
                Callouts.Close(closed.BeatId);
                break;
        }
    }

    /// <summary>
    /// Settings "Tutorial callouts" now. A change turns this run's guided first run on or off (the latest choice wins over the
    /// briefing toggle); the same value again does nothing, so the briefing's choice for this run stands.
    /// </summary>
    public void ApplySetting(bool calloutsOn)
    {
        if (calloutsOn == _setting) return;
        _setting = calloutsOn;
        if (_session.IsEnabled != calloutsOn) _session.SetEnabled(calloutsOn);
        Callouts.SetEnabled(_session.IsEnabled);
    }

    public void Dispose()
    {
        Callouts.Dismissed -= OnDismissed;
        if (_rails is not null) _rails.Changed -= SyncRails;
    }

    // A rails callout has no beat in the session behind it.
    private void OnDismissed(string beatId)
    {
        if (beatId.Contains(TutorialCallout.RailsMark, StringComparison.Ordinal)) return;
        _session.Dismiss(beatId);
    }

    private void SyncRails()
    {
        var wanted = RailsCallout(_rails?.Step);
        if (_railsCallout is { } shown && shown != wanted?.BeatId)
        {
            Callouts.Close(shown);
            _railsCallout = null;
        }
        if (wanted is not null && _railsCallout is null && Callouts.Show(wanted)) _railsCallout = wanted.BeatId;
        Callouts.Refresh();
    }

    /// <summary>The guide between two steps: what the game is doing, or the failed step's order again.</summary>
    public static TutorialCallout? RailsCallout(TutorialRailsStep? step) => step?.Phase switch
    {
        TutorialRailsPhase.Wait => new TutorialCallout(step.BeatId + TutorialCallout.RailsMark + "wait", TutorialAnchorIds.TaskBar, step.Instruction, step.Title),
        TutorialRailsPhase.Retry when step.Order is { } order => new TutorialCallout(
            step.BeatId + TutorialCallout.RailsMark + "retry", TutorialAnchorIds.CommsInput, "That didn't work. Send the same order again.", "Try again", order),
        TutorialRailsPhase.Retry => new TutorialCallout(
            step.BeatId + TutorialCallout.RailsMark + "retry", TutorialAnchorIds.OrbitMap, "The retrofire didn't go. Plan it again in the burn planner.", "Try again"),
        _ => null,
    };
}
