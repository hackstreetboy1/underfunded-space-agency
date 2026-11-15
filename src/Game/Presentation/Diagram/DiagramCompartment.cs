namespace Usa.Game.Presentation.Diagram;

/// <summary>A compartment drawn as a rounded rectangle (<c>bg.2</c>) with its label.</summary>
/// <param name="Sections">(vehicle id, section id) of the components inside, sorted.</param>
public sealed record DiagramCompartment(string Id, string Label, DiagramRect Bounds, IReadOnlyList<SectionKey> Sections);

/// <summary>A vehicle section, unique across vehicles.</summary>
public readonly record struct SectionKey(string VehicleId, string SectionId);
