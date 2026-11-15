namespace Usa.Simulation.Flight;

/// <summary>
/// Thin air above the entry interface, used only for the orbital decay of coasting vehicles (<see cref="OrbitDecay"/>):
/// an exponential from <paramref name="BaseAltitudeM"/> whose scale height changes at each layer base, with the density
/// continuous across layer bases. The integrated atmospheric flight keeps <see cref="AtmosphereModel.DensityAt"/>.
/// </summary>
/// <param name="Layers">Higher layers in ascending altitude order; null for a single exponential.</param>
public sealed record UpperAtmosphereModel(
    double BaseAltitudeM,
    double BaseDensityKgPerM3,
    double ScaleHeightM,
    IReadOnlyList<UpperAtmosphereLayer>? Layers = null)
{
    /// <summary>Density at <paramref name="altitudeM"/>; below the base the first exponential is extended downwards.</summary>
    public double DensityAt(double altitudeM)
    {
        var density = BaseDensityKgPerM3;
        var fromM = BaseAltitudeM;
        var scaleHeightM = ScaleHeightM;
        if (Layers is { } layers)
        {
            for (var i = 0; i < layers.Count && altitudeM >= layers[i].BaseAltitudeM; i++)
            {
                density *= Math.Exp(-(layers[i].BaseAltitudeM - fromM) / scaleHeightM);
                fromM = layers[i].BaseAltitudeM;
                scaleHeightM = layers[i].ScaleHeightM;
            }
        }
        return density * Math.Exp(-(altitudeM - fromM) / scaleHeightM);
    }

    public double ScaleHeightAt(double altitudeM)
    {
        var scaleHeightM = ScaleHeightM;
        if (Layers is { } layers)
        {
            for (var i = 0; i < layers.Count && altitudeM >= layers[i].BaseAltitudeM; i++) scaleHeightM = layers[i].ScaleHeightM;
        }
        return scaleHeightM;
    }
}

/// <summary>From <paramref name="BaseAltitudeM"/> up, the density falls off with <paramref name="ScaleHeightM"/>.</summary>
public sealed record UpperAtmosphereLayer(double BaseAltitudeM, double ScaleHeightM);
