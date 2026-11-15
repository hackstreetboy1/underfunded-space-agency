using Usa.Simulation.Kernel;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Presentation.Diagram;

/// <summary>A component placed on the diagram. Static structure only; its displayed state comes from knowledge.</summary>
public sealed record DiagramNode(
    ComponentId Id,
    string VehicleId,
    string SectionId,
    string Name,
    ComponentFamily Family,
    string IconKey,
    DiagramPoint Position,
    string CompartmentId,
    ControlAccess Control);
