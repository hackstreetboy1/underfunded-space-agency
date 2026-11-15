using Godot;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Time;

namespace Usa.Game.Hosting.Nodes;

/// <summary>
/// Runs the mission session on the main thread within a per-frame time budget (SCRIPTING 12.2) through a
/// <see cref="SimulationDriver"/>: paces ticks with <see cref="SimulationPacer"/>, then drains events through the
/// <see cref="EventMarshaller"/> and pumps throttled view-model notifications. Freezes come from the app-wide
/// <see cref="FreezeController"/> passed to <see cref="Initialize"/>; the host owns none of its own.
/// </summary>
public sealed partial class SimulationHost : Node
{
    /// <summary>The host's process priority, set explicitly: per-frame consumers of its flush order themselves after it.</summary>
    public const int ProcessOrder = 0;

    private SimulationDriver? _driver;

    public bool IsRunning => _driver is not null;

    public double CompressionRate => _driver?.Pacer.CompressionRate ?? 1;

    public CompressionReason CompressionReason => _driver?.Pacer.CompressionReason ?? CompressionReason.StableCoast;

    public double EffectiveRate => _driver?.Pacer.EffectiveRate ?? 0;

    public bool CanAdvanceToNextEvent => _driver?.Pacer.CanAdvanceToNextEvent ?? false;

    public bool IsAdvancingToEvent => _driver?.Pacer.IsAdvancingToEvent ?? false;

    /// <summary>The last frame's pacing result, for the debug overlay.</summary>
    public PacerFrame LastFrame { get; private set; }

    /// <param name="uiThrottle">Pumped after each event flush so throttled readouts publish on time; may be null.</param>
    /// <param name="clock">Defaults to a <see cref="StopwatchClock"/>; share it with the marshaller and throttle.</param>
    /// <param name="freeze">
    /// The app-wide freeze reasons: the session is attached to it until <see cref="Shutdown"/>. Null when the caller
    /// attaches the session itself.
    /// </param>
    public void Initialize(
        ISimulationSessionPort session,
        TimeCompressionConfig compression,
        EventMarshaller marshaller,
        UpdateThrottle? uiThrottle = null,
        PacingSettings? pacing = null,
        IMonotonicClock? clock = null,
        FreezeController? freeze = null)
    {
        _driver?.Stop();
        _driver = new SimulationDriver(session, compression, marshaller, clock ?? new StopwatchClock(), freeze, uiThrottle, pacing);
    }

    /// <summary>Stops driving the session (mission ended or scene closing); freezes stay recorded for the next one.</summary>
    public void Shutdown()
    {
        _driver?.Stop();
        _driver = null;
    }

    /// <returns>False when the vehicle is not stable enough or no event lies ahead.</returns>
    public bool RequestAdvanceToNextEvent() => _driver?.Pacer.RequestAdvanceToNextEvent() ?? false;

    public void CancelAdvanceToNextEvent() => _driver?.Pacer.CancelAdvanceToNextEvent();

    public override void _EnterTree() => ProcessPriority = ProcessOrder;

    public override void _Process(double delta)
    {
        if (_driver is null) return;
        LastFrame = _driver.Step(delta);
    }

    public override void _ExitTree() => Shutdown();
}
