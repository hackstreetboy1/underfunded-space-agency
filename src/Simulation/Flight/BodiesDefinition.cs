using Usa.Simulation.Data;

namespace Usa.Simulation.Flight;

/// <summary>Root of <c>config/bodies.json</c> (schema <c>bodies/1</c>).</summary>
public sealed record BodiesDefinition(string Schema, IReadOnlyList<BodyDefinition> Bodies) : IVersionedDefinition
{
    public const string SchemaTag = "bodies/1";
}

/// <param name="SoiRadiusM">Sphere-of-influence radius about the parent; null for the root body.</param>
/// <param name="Atmosphere">Null for an airless body.</param>
/// <param name="Orbit">Circular orbit about the parent; null for the root body.</param>
public sealed record BodyDefinition(
    string Id,
    double MuM3PerS2,
    double RadiusM,
    double? SoiRadiusM,
    double RotationRateRadPerS,
    AtmosphereModel? Atmosphere,
    BodyOrbitDefinition? Orbit);

public sealed record BodyOrbitDefinition(string Parent, double RadiusM, double PhaseAtMetZeroDeg, OrbitDirection Direction);

public enum OrbitDirection
{
    Prograde,
    Retrograde,
}
