using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Vehicle;

/// <summary>A component field changed (condition, fault, closed, armed, on, online, powered, latched, fired, deployed, jumper...).</summary>
/// <param name="Field">State path field name, as served by <see cref="VehicleComponentPaths"/>.</param>
public sealed record ComponentStateChanged(MissionTime Time, ComponentId Component, string Field, StateValue Value) : ISimEvent;

/// <summary>A tank reached zero (propellant, cryogenic reactant, gas or water).</summary>
public sealed record TankDepleted(MissionTime Time, ComponentId Tank, string Resource) : ISimEvent;

/// <summary>A bus entered or left brownout.</summary>
public sealed record BusBrownoutChanged(MissionTime Time, ComponentId Bus, double VoltageV, bool InBrownout) : ISimEvent;

/// <summary>The docking interface latched or released.</summary>
public sealed record DockingStateChanged(MissionTime Time, ComponentId Target, ComponentId Chaser, bool Latched) : ISimEvent;

/// <summary>The docking load alarm became active or cleared.</summary>
public sealed record DockingOverstressChanged(MissionTime Time, ComponentId Port, bool Active, double LoadFraction) : ISimEvent;

/// <summary>Sustained overstress damaged the docking interface.</summary>
public sealed record DockingDamaged(MissionTime Time, ComponentId Port) : ISimEvent;

/// <summary>A section separated (staging). <paramref name="EventId"/> is the section's jettison id, e.g. <c>equipment_sep</c>.</summary>
public sealed record SectionJettisoned(MissionTime Time, string VehicleId, string SectionId, string EventId) : ISimEvent;
