using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Ledger;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>
/// Both planner ports over a <see cref="ManeuverPlanner"/> and whatever propagator the <c>current</c> delegate
/// returns (the ground's estimate in the game, a fixed propagator in tests). Previews run on clones; the propagator
/// itself is never advanced. The engines handed in by the planner already carry the ground's known propellant (X8).
/// </summary>
/// <remarks>
/// A proposal works on a clone taken when it is prepared. The clone publishes no events and shares only immutable data
/// with the propagator (body, recovery zones); the planner, proposer, mission flight data and engines are immutable too,
/// so the work may run on any thread while the session keeps ticking.
/// </remarks>
public sealed class PlannerManeuverPort : IManeuverPreviewPort, IBurnProposalPort
{
    private readonly ManeuverPlanner _planner;
    private readonly BurnProposer _proposer;
    private readonly Func<OrbitPropagator?> _current;
    private readonly Func<MassBand>? _mass;
    private readonly Func<PreviewUncertainty?>? _uncertainty;

    public PlannerManeuverPort(ManeuverPlanner planner, Func<OrbitPropagator?> current)
        : this(planner, current, null, null)
    {
    }

    /// <param name="mass">The ground ledger's mass band (X8), e.g. <c>() =&gt; track.Mass</c>.</param>
    /// <param name="uncertainty">
    /// What the ground knows of the start state (tracking σ and the ledger band); previews and proposals then carry the
    /// landing dispersion. Null, or a null result, previews without it.
    /// </param>
    public PlannerManeuverPort(
        ManeuverPlanner planner, Func<OrbitPropagator?> current, Func<MassBand>? mass, Func<PreviewUncertainty?>? uncertainty = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _proposer = new BurnProposer(planner);
        _mass = mass;
        _uncertainty = uncertainty;
    }

    public MissionTime Now => _current()?.Time ?? MissionTime.Zero;

    public bool CanPreview => _current() is { } propagator && ManeuverPlanner.CanPreview(propagator);

    public double MassKg => _mass is not null ? _mass().BestKg : _current()?.MassKg ?? 0;

    public MassBand MassBand => _mass?.Invoke() ?? new MassBand(MassKg, MassKg, MassKg, string.Empty, null, KnowledgeState.Known);

    public ManeuverPreview Preview(ManeuverPlan plan) => _planner.Preview(Previewable(), plan, _uncertainty?.Invoke());

    public Func<BurnProposal> PrepareProposal(BurnGoal goal, BurnEngine engine, MissionTime earliestStart)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(engine);
        var snapshot = (_current() ?? throw new PreviewUnavailableException("No vehicle to plan for.")).Clone();
        var uncertainty = _uncertainty?.Invoke();
        var proposer = _proposer;
        return () => proposer.Propose(snapshot, goal, engine, earliestStart, uncertainty);
    }

    /// <summary>Proposes on the calling thread.</summary>
    public BurnProposal Propose(BurnGoal goal, BurnEngine engine, MissionTime earliestStart) =>
        PrepareProposal(goal, engine, earliestStart)();

    private OrbitPropagator Previewable() =>
        _current() is { } propagator && ManeuverPlanner.CanPreview(propagator)
            ? propagator
            : throw new PreviewUnavailableException("No coasting vehicle to preview.");
}
