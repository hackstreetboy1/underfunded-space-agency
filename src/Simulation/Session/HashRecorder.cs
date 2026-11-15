using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>A state hash at a MET.</summary>
public readonly record struct StateHashSample(MissionTime Time, string Hash);

/// <summary>Records the session state hash every 60 s MET (SCRIPTING 6.3); registered last so it sees the finished tick.</summary>
internal sealed class HashRecorder(Func<string> compute, EventBus events) : ISimSystem
{
    public const int PeriodSeconds = 60;

    private readonly List<StateHashSample> _samples = [];

    public int PeriodTicks => PeriodSeconds * SimulationClock.TicksPerSecond;

    public IReadOnlyList<StateHashSample> Samples => _samples;

    public void Step(in StepContext context)
    {
        var hash = compute();
        _samples.Add(new StateHashSample(context.Now, hash));
        events.Publish(new StateHashRecorded(context.Now, hash));
    }
}
