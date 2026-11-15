using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>One vehicle's attitude for a frame: what the 3D view poses it from.</summary>
/// <param name="Orientation">Vehicle → inertial.</param>
/// <param name="DockedWith">The other vehicle of the docked stack, or null when flying alone.</param>
public readonly record struct VehicleAttitude(Quatd Orientation, double RollDps, double PitchDps, double YawDps, double TotalDps, string? DockedWith);

/// <summary>
/// What can have changed in the session since a consumer last read it: MET, the freeze, and the simulation events seen. Two
/// equal stamps mean the session state a frame shows is the same (every state change outside time passing publishes an event).
/// </summary>
public readonly record struct SessionFrameStamp(MissionTime Now, FreezeReason Freeze, int Events);

/// <summary>
/// The narrow per-frame reads of the session (review 2, R5/A6): a change stamp, a knowledge revision and vehicle attitudes, so
/// per-frame consumers never build a <see cref="SessionSnapshot"/>. The full snapshot is built only on request
/// (<see cref="Snapshot"/>, the 10 Hz pump) and reused until the stamp changes. Feed every drained event to <see cref="Observe"/>.
/// </summary>
/// <remarks>Attitudes come straight from <c>MissionSession.TryGetAttitude</c>, which allocates nothing (PERFORMANCE.md, "Game-side per-frame allocations").</remarks>
public sealed class SessionFrameReader
{
    private readonly MissionSession _session;
    private SessionSnapshot? _snapshot;
    private SessionFrameStamp _snapshotStamp;
    private int _events;

    public SessionFrameReader(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public SessionFrameStamp Stamp => new(_session.Now, _session.FreezeReasons, _events);

    /// <summary>Bumps on every knowledge event (component looks, fact states, ground bands); equal values mean nothing known changed.</summary>
    public int KnowledgeRevision { get; private set; }

    public void Observe(ISimEvent simEvent)
    {
        _events++;
        if (simEvent is ComponentKnowledgeChanged or KnowledgeChanged or GroundBandChanged) KnowledgeRevision++;
    }

    /// <summary>The full snapshot (allocates), rebuilt only when <see cref="Stamp"/> changed since the last build.</summary>
    public SessionSnapshot Snapshot()
    {
        var stamp = Stamp;
        if (_snapshot is null || stamp != _snapshotStamp)
        {
            _snapshot = _session.Snapshot();
            _snapshotStamp = stamp;
        }
        return _snapshot;
    }

    public bool TryGetAttitude(string vehicleId, out VehicleAttitude attitude)
    {
        ArgumentNullException.ThrowIfNull(vehicleId);
        if (!_session.TryGetAttitude(vehicleId, out var reading))
        {
            attitude = default;
            return false;
        }
        attitude = new VehicleAttitude(reading.Orientation, reading.RollDps, reading.PitchDps, reading.YawDps, reading.TotalDps, reading.DockedWithId);
        return true;
    }
}
