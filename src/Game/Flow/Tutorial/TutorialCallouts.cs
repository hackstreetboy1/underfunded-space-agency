using Usa.Simulation.Guidance;

namespace Usa.Game.Flow.Tutorial;

/// <summary>
/// One guided-first-run callout (GDD 9.4): title (optional), one or two sentences, the order it offers to send (typed-order
/// steps) and "Continue" (informational steps) or "Got it".
/// </summary>
/// <param name="Anchor">UI anchor id from <c>tutorial.json</c>, e.g. <c>resource_strip</c>.</param>
/// <param name="Order">The structured order behind the callout's send button; null when the step is not a typed order.</param>
public sealed record TutorialCallout(string BeatId, string Anchor, string Text, string? Title = null, TutorialOrder? Order = null)
{
    /// <summary>Nothing to do but read it: no order to send and no control whose press completes it (its button says "Continue").</summary>
    public bool IsInformational => Order is null && !TutorialAnchorIds.CompletesOnPress.Contains(Anchor, StringComparer.Ordinal);

    /// <summary>The send button's words: "Send: ‘June, read the thruster temps’".</summary>
    public string? OrderLabel => Order is { } order ? $"Send: ‘{order.Text}’" : null;

    /// <summary>Marks a callout the rails add between the director's beats ("walk_dock~wait", "walk_open_t8~retry").</summary>
    public const char RailsMark = '~';

    /// <summary>A rails wait or retry callout: no session beat stands behind it.</summary>
    public bool IsRails => BeatId.Contains(RailsMark, StringComparison.Ordinal);

    /// <summary>The beat the callout belongs to (its own id, or the step a rails callout follows).</summary>
    public string StepBeatId => IsRails ? BeatId[..BeatId.IndexOf(RailsMark, StringComparison.Ordinal)] : BeatId;
}

/// <summary>
/// The callout layer of the guided first run (UI.md 5.1): at most one callout at a time, the rest queued in order.
/// It never blocks input or pauses anything, and time-compression changes do not touch it, so the queue survives
/// them. <see cref="TutorialCalloutFeed"/> drives it from the session with <see cref="Show"/> and <see cref="Close"/>; "Continue" / "Got it" is
/// <see cref="Dismiss"/>, which the feed submits to the session as a command through <see cref="Dismissed"/>.
/// </summary>
public sealed class TutorialCallouts
{
    public const int MaxWidthPx = 280;

    private readonly List<TutorialCallout> _pending = [];

    public TutorialCallouts(bool enabled)
    {
        Enabled = enabled;
    }

    public event Action? Changed;

    /// <summary>"Continue" / "Got it" on a callout (beat id).</summary>
    public event Action<string>? Dismissed;

    /// <summary>The send button of the visible callout: the host submits the order as a structured request.</summary>
    public event Action<TutorialOrder>? OrderRequested;

    /// <summary>Guided first run on for this run and callouts allowed by Settings.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Callouts are anchored hints; clicks and keys always reach the console.</summary>
    public static bool BlocksInput => false;

    public TutorialCallout? Current { get; private set; }

    /// <summary>The guided first run's tutorial rails; null when the run has none.</summary>
    public ITutorialRails? Rails { get; private set; }

    /// <summary>The railed step <paramref name="callout"/> shows ("Step 4 of 23" and the exact action); null outside the rails.</summary>
    public TutorialRailsStep? StepOf(TutorialCallout callout)
    {
        ArgumentNullException.ThrowIfNull(callout);
        return Rails?.Step is { } step && step.BeatId == callout.StepBeatId ? step : null;
    }

    /// <summary>Attaches the rails (the callouts then number their steps and drop the skip button on action steps).</summary>
    public void AttachRails(ITutorialRails? rails)
    {
        Rails = rails;
        Changed?.Invoke();
    }

    /// <summary>The rails moved on without a callout change: the view redraws the step line.</summary>
    public void Refresh() => Changed?.Invoke();

    public IReadOnlyList<TutorialCallout> Pending => _pending;

    /// <summary>Queues the callout; ignored when disabled or when the beat is already shown or queued.</summary>
    public bool Show(TutorialCallout callout)
    {
        ArgumentNullException.ThrowIfNull(callout);
        if (!Enabled || IsKnown(callout.BeatId)) return false;
        if (Current is null) Current = callout;
        else _pending.Add(callout);
        Changed?.Invoke();
        return true;
    }

    /// <summary>The director closed the beat (completed, timed out or ended); removes it wherever it is.</summary>
    public bool Close(string beatId)
    {
        if (Current?.BeatId == beatId)
        {
            Advance();
            return true;
        }

        var index = _pending.FindIndex(c => c.BeatId == beatId);
        if (index < 0) return false;
        _pending.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    /// <summary>"Got it" on the visible callout.</summary>
    public bool Dismiss()
    {
        if (Current is not { } current) return false;
        Advance();
        Dismissed?.Invoke(current.BeatId);
        return true;
    }

    /// <summary>
    /// The send button on the visible callout. The callout stays: it closes when the session sees the step start (or the player
    /// presses Continue), so a refused order (crew busy) leaves the guide where it is.
    /// </summary>
    public bool RequestOrder()
    {
        if (!Enabled || Current?.Order is not { } order) return false;
        OrderRequested?.Invoke(order);
        return true;
    }

    /// <summary>Settings switched callouts off mid-run: everything clears and nothing new shows.</summary>
    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (!enabled)
        {
            Current = null;
            _pending.Clear();
        }
        Changed?.Invoke();
    }

    private bool IsKnown(string beatId) => Current?.BeatId == beatId || _pending.Exists(c => c.BeatId == beatId);

    private void Advance()
    {
        if (_pending.Count > 0)
        {
            Current = _pending[0];
            _pending.RemoveAt(0);
        }
        else
        {
            Current = null;
        }
        Changed?.Invoke();
    }
}
