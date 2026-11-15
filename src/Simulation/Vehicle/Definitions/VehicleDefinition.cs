using Usa.Simulation.Data;

namespace Usa.Simulation.Vehicle.Definitions;

/// <summary>
/// A vehicle as authored in <c>data/vehicles/&lt;id&gt;.json</c> (schema <c>vehicle/1</c>). Body axes: X = nose/roll,
/// Y = pitch, Z = yaw. Positions are in metres in the vehicle reference frame; mass properties are computed from
/// the attached sections plus tank and retro-rocket contents.
/// </summary>
public sealed record VehicleDefinition : IVersionedDefinition
{
    public required string Schema { get; init; }

    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<SectionDefinition> Sections { get; init; }

    public required IReadOnlyList<ComponentDefinition> Components { get; init; }

    /// <summary>Conduction links between thermal-node components, for the life-support thermal model.</summary>
    public IReadOnlyList<ThermalLinkDefinition> ThermalLinks { get; init; } = [];

    /// <summary>Pressurized cabin parameters, for the life-support atmosphere model. Null for unmanned vehicles.</summary>
    public CabinDefinition? Cabin { get; init; }

    public ProvisionsDefinition? Provisions { get; init; }
}

/// <summary>A separable part of the vehicle. Components belong to exactly one section.</summary>
public sealed record SectionDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Everything in the section except tank and retro-rocket contents.</summary>
    public required double DryMassKg { get; init; }

    /// <summary>Section centre of mass [x, y, z] in the vehicle frame.</summary>
    public required IReadOnlyList<double> CenterOfMassM { get; init; }

    /// <summary>Principal moments of the section's dry mass about its own centre of mass, aligned with the body axes.</summary>
    public required InertiaDefinition InertiaKgM2 { get; init; }

    /// <summary>Staging event id that separates this section (e.g. <c>equipment_sep</c>); null if it never separates.</summary>
    public string? Jettison { get; init; }
}

public sealed record InertiaDefinition(double Roll, double Pitch, double Yaw);
