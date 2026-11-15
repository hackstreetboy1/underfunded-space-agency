using Usa.Simulation.Attitude;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// The crewed vehicle's peak absolute roll rate since load, sampled every 50 ms tick right after attitude and served as
/// <c>attitude.&lt;crewed&gt;.peak_roll_dps</c> (read only). It is simulation state (hashed), so a resumed run rebuilt by
/// <see cref="MissionSession.Replay"/> reports the same peak; the debrief reads it instead of sampling rendered frames.
/// </summary>
internal sealed class PeakRollTracker : ISimSystem, IStateProvider, IHashContributor
{
    public const string Field = "peak_roll_dps";

    private readonly AttitudeVehicle _vehicle;

    public PeakRollTracker(AttitudeSystem attitude, string crewedVehicleId)
    {
        _vehicle = attitude.GetVehicle(crewedVehicleId);
        Prefix = $"{StatePaths.Attitude}{crewedVehicleId}.{Field}";
    }

    /// <summary>The full path: longer than the attitude provider's prefix, so it wins for this one path only.</summary>
    public string Prefix { get; }

    public int PeriodTicks => 1;

    public double PeakRollDps { get; private set; }

    public void Step(in StepContext context)
    {
        var roll = Math.Abs(_vehicle.RateRollDegPerSec);
        if (roll > PeakRollDps) PeakRollDps = roll;
    }

    public bool TryGet(string path, out StateValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path == Prefix)
        {
            value = PeakRollDps;
            return true;
        }
        value = StateValue.None;
        return false;
    }

    public bool IsKnownPath(string path) => path == Prefix;

    public void WriteHash(StateHashWriter writer) => writer.Write(PeakRollDps);
}
