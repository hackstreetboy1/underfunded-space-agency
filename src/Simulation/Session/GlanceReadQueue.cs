using Usa.Simulation.Commands;
using Usa.Simulation.Crew;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;

namespace Usa.Simulation.Session;

/// <summary>
/// Delayed glance reads (no task slot): at the first second step at or after <see cref="GlanceRead.ReadyAt"/> the reader reports
/// every fact of the read, as read from the gauges then (<see cref="FactValueReader"/>). A reader who died or went outside
/// meanwhile reports nothing.
/// </summary>
internal sealed class GlanceReadQueue(KnowledgeBase knowledge, FactValueReader reader, CrewRoster roster) : IGlanceReadExecutor, ISimSystem, IHashContributor
{
    private readonly List<GlanceRead> _pending = [];

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public IReadOnlyList<GlanceRead> Pending => _pending;

    public void Schedule(GlanceRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var at = _pending.Count;
        while (at > 0 && _pending[at - 1].ReadyAt > read.ReadyAt) at--;
        _pending.Insert(at, read);
    }

    public void Step(in StepContext context)
    {
        while (_pending.Count > 0 && _pending[0].ReadyAt <= context.Now)
        {
            var read = _pending[0];
            _pending.RemoveAt(0);
            if (!roster.TryFind(read.Reader, out var member) || !member.IsAlive || member.Location != CrewLocation.Cabin) continue;
            foreach (var fact in read.Facts)
            {
                if (reader.Read(fact, read.Reader, ObservationMethod.Glance, context.Now, []) is { } report) knowledge.Submit(report with { Origin = read.Origin });
            }
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        writer.Write((long)_pending.Count);
        foreach (var read in _pending)
        {
            writer.Write(read.Origin.Value ?? "");
            writer.Write(read.ReadyAt.Milliseconds);
        }
    }
}
