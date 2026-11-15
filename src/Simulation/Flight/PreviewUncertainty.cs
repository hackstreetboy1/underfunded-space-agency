namespace Usa.Simulation.Flight;

/// <summary>How well the ground knows the state a preview starts from (X8): position σ from tracking and the ledger's mass band.</summary>
/// <param name="SigmaRadialM">1σ radial position error.</param>
/// <param name="SigmaAlongM">1σ along-track position error.</param>
/// <param name="MassLowKg">Low edge of the ledger mass band (a <paramref name="DisplaySigma"/> band).</param>
/// <param name="MassHighKg">High edge of the ledger mass band.</param>
/// <param name="DisplaySigma">How many σ the mass band and the resulting landing arc span (navigation.json <c>displaySigma</c>).</param>
public sealed record PreviewUncertainty(double SigmaRadialM, double SigmaAlongM, double MassLowKg, double MassHighKg, double DisplaySigma = 2)
{
    public static PreviewUncertainty None(double massKg) => new(0, 0, massKg, massKg);

    internal void RequireValid()
    {
        if (!(SigmaRadialM >= 0) || !(SigmaAlongM >= 0) || !double.IsFinite(SigmaRadialM) || !double.IsFinite(SigmaAlongM))
            throw new ArgumentOutOfRangeException(nameof(SigmaRadialM), "Position σ must be finite and not negative.");
        if (!(MassLowKg > 0) || !(MassHighKg >= MassLowKg) || !double.IsFinite(MassHighKg))
            throw new ArgumentOutOfRangeException(nameof(MassLowKg), "The mass band must be positive and ordered.");
        if (!(DisplaySigma > 0)) throw new ArgumentOutOfRangeException(nameof(DisplaySigma), DisplaySigma, "Must be positive.");
    }
}

/// <summary>
/// Where the previewed landing may really fall (X8): a <see cref="DisplaySigma"/> arc on the rim around the predicted
/// landing, from the start-state σ and the mass band, and the Δv band the mass band gives.
/// </summary>
/// <param name="HalfWidthRad">Half the arc, as an angle at the body's centre (a longitude half-width).</param>
/// <param name="HalfWidthKm">Half the arc along the surface.</param>
/// <param name="DeltaVLowMps">Δv at the heavy end of the mass band.</param>
/// <param name="DeltaVHighMps">Δv at the light end.</param>
/// <param name="WorstRecoveryWaitHours">The longest recovery wait anywhere inside the arc (zones or open sea).</param>
/// <param name="InZone">The whole arc lies inside the predicted landing's zone (IN ZONE); otherwise NEAR EDGE.</param>
public sealed record LandingDispersion(
    double HalfWidthRad,
    double HalfWidthKm,
    double DisplaySigma,
    double DeltaVLowMps,
    double DeltaVHighMps,
    double WorstRecoveryWaitHours,
    bool InZone);
