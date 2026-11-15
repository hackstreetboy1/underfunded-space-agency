using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>The outcome of "Propose burn" with its one-line explanation.</summary>
/// <param name="Applied">The proposal filled the planner (the player still presses Execute).</param>
public sealed record ProposalOutcome(BurnGoal Goal, bool Applied, string ExplanationKey, string Explanation);

/// <summary>One-liners (<c>planner.proposal*</c> strings) for <see cref="BurnProposer"/> explanation keys.</summary>
public sealed class ProposalText
{
    private readonly MapText _text;

    public ProposalText(MapText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    public string Explain(BurnProposal proposal, Func<string, string> zoneLabel)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(zoneLabel);
        (string, string) Duration() => ("duration", FlightFormat.Duration(Arg(proposal, "duration_s")));
        (string, string) Start() => ("start", Arg(proposal, "start_met_s") is var s && double.IsFinite(s) ? _text.Met(MissionTime.FromSeconds(s)) : FlightFormat.Placeholder);
        (string, string) Pe() => ("periapsis", FlightFormat.AltitudeKm(Arg(proposal, "periapsis_km")));
        (string, string) Ap() => ("apoapsis", FlightFormat.AltitudeKm(Arg(proposal, "apoapsis_km")));

        return proposal.ExplanationKey switch
        {
            BurnProposer.KeyLowerPeriapsis => _text.Format("planner.proposalLowerPeriapsis", Duration(), Start(), Pe()),
            BurnProposer.KeyRaiseOrbit => _text.Format("planner.proposalRaiseOrbit", Duration(), Start(), Pe(), Ap()),
            BurnProposer.KeyTargetZone when proposal.Goal is TargetZone zone => _text.Format(
                "planner.proposalTargetZone", Duration(), Start(), ("zone", zoneLabel(zone.ZoneId)),
                ("angle", FlightFormat.AngleDeg(Arg(proposal, "entry_angle_deg")))),
            BurnProposer.KeyAlreadySatisfied => _text.Get("planner.proposalAlreadySatisfied"),
            BurnProposer.KeyInsufficientDeltaV => proposal.Arguments.Count > 0
                ? _text.Format("planner.proposalInsufficientDeltaVLimit", ("deltaV", FlightFormat.Speed(Arg(proposal, "delta_v_mps"))))
                : _text.Get("planner.proposalInsufficientDeltaV"),
            BurnProposer.KeyZoneUnreachable => _text.Get("planner.proposalZoneUnreachable"),
            BurnProposer.KeyUnknownZone => _text.Get("planner.proposalUnknownZone"),
            BurnProposer.KeyUnavailable => _text.Get("planner.proposalUnavailable"),
            _ => _text.Get(proposal.HasPlan ? "planner.proposalReady" : "planner.proposalNotFound"),
        };
    }

    private static double Arg(BurnProposal proposal, string name)
    {
        foreach (var argument in proposal.Arguments)
        {
            if (argument.Name == name) return argument.Value;
        }
        return double.NaN;
    }
}
