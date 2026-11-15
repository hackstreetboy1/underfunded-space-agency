using Usa.Game.Hosting;
using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;
using Usa.Simulation.Time;

namespace Usa.Game.Integration;

/// <summary>
/// Closes the hosting port over <see cref="MissionSession"/>, so <c>SimulationHost</c>, the pacer, the marshaller and the
/// freeze controller drive a real session without depending on its full API.
/// </summary>
public sealed class SessionPortAdapter : ISimulationSessionPort<CommandRequest, ValidationResult, SessionSnapshot>
{
    public SessionPortAdapter(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    /// <summary>The wrapped session, for presentation code that needs more than the port (catalogs, knowledge, replays).</summary>
    public MissionSession Session { get; }

    /// <summary>The player is being asked to confirm an order (comms); set by the runtime once composed.</summary>
    public Func<bool>? ConfirmationPending { get; set; }

    /// <summary>The mark of the burn order the runtime holds (<c>PlanExecutor.Pending</c>); set by the runtime once composed.</summary>
    public Func<MissionTime?>? HeldOrderAt { get; set; }

    public MissionTime Now => Session.Now;

    public bool IsFrozen => Session.IsFrozen;

    public FreezeReason FreezeReasons => Session.FreezeReasons;

    public IStateQuery State => Session.State;

    public void Freeze(FreezeReason reason) => Session.Freeze(reason);

    public void Unfreeze(FreezeReason reason) => Session.Unfreeze(reason);

    public int Advance(MissionTime target, int maxTicks) => Session.Advance(target, maxTicks);

    public void DrainEvents(Action<ISimEvent> sink) => Session.DrainEvents(sink);

    /// <summary>
    /// The session's compression inputs stamped with the current MET. A next-event time that is not a finite,
    /// positive number of seconds is dropped, so the director never schedules an advance into the past. The runtime adds what
    /// only the host knows: a pending confirmation and a held burn order (its mark; a tutorial callout no longer waits for the
    /// player once the order is held).
    /// </summary>
    public CompressionInputs ReadCompressionInputs()
    {
        var inputs = Session.CompressionInputs();
        var now = Session.Now;
        var next = inputs.SecondsToNextEventMet is { } seconds && double.IsFinite(seconds) && seconds > 0 ? seconds : (double?)null;
        inputs = inputs with { Now = now, SecondsToNextEventMet = next, PlayerConfirmationPending = ConfirmationPending?.Invoke() ?? false };
        if (HeldOrderAt?.Invoke() is { } mark && mark >= now)
            inputs = inputs with { SecondsToHeldOrderMet = (mark.Milliseconds - now.Milliseconds) / 1000.0, TutorialActionMaxRate = null };
        return inputs;
    }

    public ValidationResult Submit(CommandRequest request) => Session.Submit(request);

    public SessionSnapshot Snapshot() => Session.Snapshot();
}
