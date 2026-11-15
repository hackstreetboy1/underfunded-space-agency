namespace Usa.Simulation.Flight;

/// <summary>
/// Isothermal exponential atmosphere, ρ(h) = ρ0 e^(-h/H), zero above the entry interface. With an
/// <see cref="UpperAtmosphere"/>, the last <see cref="JoinDepthScaleHeights"/> scale heights below its base bend
/// log-linearly onto the base density, so density is continuous from the ground up through the thin air.
/// </summary>
/// <param name="UpperAtmosphere">Thin air for orbital decay from its base altitude (the interface) up; null continues the sea-level exponential.</param>
public sealed record AtmosphereModel(
    double ScaleHeightM,
    double SeaLevelDensityKgPerM3,
    double InterfaceAltitudeM,
    UpperAtmosphereModel? UpperAtmosphere = null)
{
    /// <summary>Depth of the join below the upper atmosphere's base, in sea-level scale heights.</summary>
    public const double JoinDepthScaleHeights = 2;

    public double DensityAt(double altitudeM) => altitudeM >= InterfaceAltitudeM ? 0 : LowerDensityAt(altitudeM);

    /// <summary>
    /// Density for the slow orbital decay of coasting vehicles (<see cref="OrbitDecay"/>): the upper atmosphere from its
    /// base up, the (joined) lower atmosphere below it. The integrated flight model keeps the cut-off of <see cref="DensityAt"/>.
    /// </summary>
    public double ThinDensityAt(double altitudeM) =>
        UpperAtmosphere is { } upper && altitudeM >= upper.BaseAltitudeM ? upper.DensityAt(altitudeM) : LowerDensityAt(altitudeM);

    /// <summary>Local scale height of <see cref="ThinDensityAt"/>.</summary>
    public double ThinScaleHeightAt(double altitudeM)
    {
        if (UpperAtmosphere is not { } upper) return ScaleHeightM;
        if (altitudeM >= upper.BaseAltitudeM) return upper.ScaleHeightAt(altitudeM);
        var joinStartM = JoinStartM(upper);
        return altitudeM > joinStartM
            ? (upper.BaseAltitudeM - joinStartM) / Math.Log(Exponential(joinStartM) / upper.BaseDensityKgPerM3)
            : ScaleHeightM;
    }

    /// <summary>Where the join to <paramref name="upper"/> begins (below it the sea-level exponential holds).</summary>
    public double JoinStartM(UpperAtmosphereModel upper)
    {
        ArgumentNullException.ThrowIfNull(upper);
        return upper.BaseAltitudeM - JoinDepthScaleHeights * ScaleHeightM;
    }

    private double LowerDensityAt(double altitudeM)
    {
        if (UpperAtmosphere is not { } upper) return Exponential(altitudeM);
        var joinStartM = JoinStartM(upper);
        if (altitudeM <= joinStartM) return Exponential(altitudeM);
        var startDensity = Exponential(joinStartM);
        var fraction = (altitudeM - joinStartM) / (upper.BaseAltitudeM - joinStartM);
        return startDensity * Math.Exp(fraction * Math.Log(upper.BaseDensityKgPerM3 / startDensity));
    }

    private double Exponential(double altitudeM) => SeaLevelDensityKgPerM3 * Math.Exp(-Math.Max(altitudeM, 0) / ScaleHeightM);
}
