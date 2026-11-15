using Usa.Game.Presentation.Workspace;
using Usa.Simulation.Crew;
using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Repair candidates over the Crew area's performance model (GDD 5.4): duration factor = skill × health (× rate impairment)
/// relative to the spec's duration, success = 1 − error probability for the spec's complexity. Health is the crew's visible
/// biomedical number; the spin rate is the latest the crew reported (body-rate facts), never the true rate.
/// </summary>
public sealed class RepairAssignmentEstimator : IRepairAssignmentEstimator
{
    private readonly MissionSession _session;
    private readonly CrewNames _names;
    private readonly KnowledgeReadouts _readouts;
    private readonly FactId[] _rateFacts;

    public RepairAssignmentEstimator(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _names = new CrewNames(session.Data.Crew);
        _readouts = new KnowledgeReadouts(session.Data.Facts, session.Knowledge, _names);
        _rateFacts = session.Data.Facts.Facts.Where(f => f.Id.Value.StartsWith($"{session.Data.Flight.Vehicles.CrewedId}.rate.", StringComparison.Ordinal)).Select(f => f.Id).ToArray();
    }

    public IReadOnlyList<AssigneeEstimate> Estimate(RepairTaskSpec task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var performance = _session.Data.Crew.Performance;
        var impaired = performance.IsRateImpaired(KnownRateDps());
        var domain = Domain(task.Skill);
        _session.Data.Tasks.TryGet(task.TaskId, out var definition);

        var estimates = new List<AssigneeEstimate>();
        foreach (var crew in _session.Data.Scenario.Mission.Crew)
        {
            if (!_names.TryGet(crew, out var member)) continue;
            var alive = Bool($"crew.{crew.Value}.alive");
            var onEva = Text($"crew.{crew.Value}.location") == "eva";
            var health = alive ? Number($"crew.{crew.Value}.health") : 0;
            var skill = domain is { } d ? member.Skill(d) : definition is not null ? CrewPerformance.SkillLevel(definition, member) : 1;

            double factor = 1;
            if (definition is not null && definition.BaseDurationS > 0)
                factor = performance.DurationSeconds(definition, skill, health, impaired) / definition.BaseDurationS;
            var success = 1 - performance.ErrorProbability(task.Complexity, member.Experience, health, impaired);

            var reason = !alive ? "Lost" : onEva ? "On EVA" : string.Empty;
            estimates.Add(new AssigneeEstimate(crew, member.Name, reason.Length == 0, reason, factor, Math.Clamp(success, 0, 1)));
        }
        return estimates;
    }

    /// <summary>Total body rate from the crew's latest rate readings; 0 when nobody has read them.</summary>
    private double KnownRateDps()
    {
        var sum = 0.0;
        foreach (var fact in _rateFacts)
        {
            if (_readouts.LatestMeasurement(fact) is { Value: { } rate }) sum += rate * rate;
        }
        return Math.Sqrt(sum);
    }

    private static SkillDomain? Domain(string skill) => skill switch
    {
        "flight_ops" => SkillDomain.FlightOps,
        "attitude" => SkillDomain.Attitude,
        "navigation" => SkillDomain.Navigation,
        "propulsion" => SkillDomain.Propulsion,
        "electrical" => SkillDomain.Electrical,
        "life_support" => SkillDomain.LifeSupport,
        "thermal" => SkillDomain.Thermal,
        "eva" => SkillDomain.Eva,
        "medical" => SkillDomain.Medical,
        _ => null,
    };

    private bool Bool(string path) => _session.State.TryGet(path, out var value) && value.Kind == StateValueKind.Bool && value.AsBool();

    private double Number(string path) => _session.State.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? value.AsNumber() : 0;

    private string Text(string path) => _session.State.TryGet(path, out var value) && value.Kind == StateValueKind.Text ? value.AsText() : string.Empty;
}
