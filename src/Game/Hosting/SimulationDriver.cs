using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Time;

namespace Usa.Game.Hosting;

/// <summary>
/// Godot-free core of <c>SimulationHost</c> for one running session: paces it each frame, then drains its events through
/// the marshaller and pumps the UI throttle. While it runs, the session is attached to the injected app-wide
/// <see cref="FreezeController"/>, so freezes set before or during the run reach it.
/// </summary>
public sealed class SimulationDriver
{
    private readonly ISimulationSessionPort _session;
    private readonly EventMarshaller _marshaller;
    private readonly FreezeController? _freeze;
    private readonly UpdateThrottle? _uiThrottle;

    /// <param name="freeze">The app-wide freeze reasons; null when the caller attaches the session itself.</param>
    /// <param name="uiThrottle">Pumped after each event flush so throttled readouts publish on time; may be null.</param>
    public SimulationDriver(
        ISimulationSessionPort session,
        TimeCompressionConfig compression,
        EventMarshaller marshaller,
        IMonotonicClock clock,
        FreezeController? freeze = null,
        UpdateThrottle? uiThrottle = null,
        PacingSettings? pacing = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(compression);
        ArgumentNullException.ThrowIfNull(marshaller);
        ArgumentNullException.ThrowIfNull(clock);
        _session = session;
        _marshaller = marshaller;
        _freeze = freeze;
        _uiThrottle = uiThrottle;
        Pacer = new SimulationPacer(session, new TimeCompressionDirector(compression), clock, pacing);
        freeze?.Attach(session);
    }

    public SimulationPacer Pacer { get; }

    public bool IsStopped { get; private set; }

    public PacerFrame Step(double realDeltaS)
    {
        var frame = Pacer.Step(realDeltaS);
        _marshaller.Flush(_session);
        _uiThrottle?.Pump();
        return frame;
    }

    /// <summary>Detaches the session from the freeze controller (if still attached); the reasons stay recorded.</summary>
    public void Stop()
    {
        if (IsStopped) return;
        IsStopped = true;
        _freeze?.Detach(_session);
    }
}
