using Usa.Simulation.Data;

namespace Usa.Simulation.Flight;

/// <summary>
/// A fixed inertial Sun line in the orbit plane (β = 0, M2 §16 E4) and the body's cylindrical shadow: the sunlit fraction
/// ramps linearly across the shadow edge over <paramref name="PenumbraS"/> of flight. Declared in <c>flight.json</c>
/// <c>sun</c>; a mission's <c>session.json</c> <c>sun</c> overrides it.
/// </summary>
/// <param name="InertialAngleDeg">Direction to the Sun, same convention as <c>attitude.&lt;v&gt;.pointing_deg</c>.</param>
public sealed record SunDefinition(double InertialAngleDeg, double PenumbraS = 20)
{
    /// <summary>Adds a problem for a non-finite angle or a negative or non-finite penumbra; <paramref name="path"/> is the block's JSON path.</summary>
    public void Validate(string file, string path, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (!double.IsFinite(InertialAngleDeg)) errors.Add(file, path + ".inertialAngleDeg", "Must be a finite angle.");
        if (!(PenumbraS >= 0 && double.IsFinite(PenumbraS))) errors.Add(file, path + ".penumbraS", "Must be a finite number >= 0.");
    }
}
