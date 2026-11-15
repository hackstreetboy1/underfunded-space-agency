using System.Diagnostics;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Hosting;

/// <summary>
/// The app's one freeze signal (GDD 3.2). Combines independent freeze requests (Esc menu, AI service outage, loading)
/// and forwards each reason to the attached session exactly once when it starts or ends. Works before a session exists:
/// on <see cref="Attach"/> the session is reconciled to the active reasons. Everything else that must hold while the
/// game is frozen (comms, confirmation timers, speech, mic, audio) follows <see cref="Frozen"/>. Subscribers to
/// <see cref="Frozen"/> and <see cref="Reasons"/> are notified in isolation: one that throws is reported and the others
/// still follow, so a broken caption handler can never leave the mic or comms frozen while the simulation runs.
/// </summary>
public sealed class FreezeController
{
    private static readonly FreezeReason[] KnownReasons = [FreezeReason.Menu, FreezeReason.ServiceOutage, FreezeReason.Loading];
    private const FreezeReason AllKnown = FreezeReason.Menu | FreezeReason.ServiceOutage | FreezeReason.Loading;

    private readonly IsolatedObservable<FreezeReason> _reasons;
    private readonly IsolatedObservable<bool> _frozen;
    private ISimulationSessionPort? _session;

    /// <param name="onSubscriberError">Where a throwing subscriber is reported (the app log); defaults to <see cref="Trace"/>.</param>
    public FreezeController(Action<Exception>? onSubscriberError = null)
    {
        var report = onSubscriberError ?? (static ex => Trace.TraceError($"freeze subscriber failed: {ex}"));
        _reasons = new IsolatedObservable<FreezeReason>(FreezeReason.None, report);
        _frozen = new IsolatedObservable<bool>(false, report);
    }

    /// <summary>Active reasons; the freeze overlay binds to this.</summary>
    public IReadOnlyObservable<FreezeReason> Reasons => _reasons;

    /// <summary>True while any reason is active; changes only when the first reason starts or the last one ends.</summary>
    public IReadOnlyObservable<bool> Frozen => _frozen;

    public bool IsFrozen => _reasons.Value != FreezeReason.None;

    /// <summary>
    /// Connects a (new) session and reconciles it to the active reasons: missing ones are applied and ones the session
    /// still holds from an earlier owner are released. Null detaches.
    /// </summary>
    public void Attach(ISimulationSessionPort? session)
    {
        _session = session;
        if (session is null) return;
        var held = session.FreezeReasons & AllKnown;
        ForEachReason(_reasons.Value & ~held, session.Freeze);
        ForEachReason(held & ~_reasons.Value, session.Unfreeze);
    }

    /// <summary>Detaches <paramref name="session"/> only if it is the attached one, so a stale owner cannot detach a newer session.</summary>
    public void Detach(ISimulationSessionPort session)
    {
        if (ReferenceEquals(_session, session)) _session = null;
    }

    public void Freeze(FreezeReason reason) => Set(reason, frozen: true);

    public void Unfreeze(FreezeReason reason) => Set(reason, frozen: false);

    /// <param name="reason">One or more of <see cref="FreezeReason.Menu"/>, <see cref="FreezeReason.ServiceOutage"/>, <see cref="FreezeReason.Loading"/>.</param>
    public void Set(FreezeReason reason, bool frozen)
    {
        if (reason == FreezeReason.None || (reason & ~AllKnown) != 0)
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Expected one or more known freeze reasons.");

        var current = _reasons.Value;
        var changed = frozen ? reason & ~current : reason & current;
        if (changed == FreezeReason.None) return;

        if (_session is { } session) ForEachReason(changed, frozen ? session.Freeze : session.Unfreeze);
        _reasons.Set(frozen ? current | changed : current & ~changed);
        _frozen.Set(_reasons.Value != FreezeReason.None);
    }

    private static void ForEachReason(FreezeReason reasons, Action<FreezeReason> apply)
    {
        foreach (var known in KnownReasons)
        {
            if ((reasons & known) != 0) apply(known);
        }
    }
}
