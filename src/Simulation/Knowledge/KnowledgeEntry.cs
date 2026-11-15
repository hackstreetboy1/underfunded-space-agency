using System.Text.Json.Serialization;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>
/// One step in the knowledge journal. The journal fully determines the knowledge base: saving it and
/// replaying it with <see cref="KnowledgeBase.Restore"/> rebuilds identical state.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ObservationEntry), "observation")]
[JsonDerivedType(typeof(TellEntry), "tell")]
[JsonDerivedType(typeof(SuspicionEntry), "suspicion")]
[JsonDerivedType(typeof(RepairEntry), "repair")]
public abstract record KnowledgeEntry(MissionTime Time);

public sealed record ObservationEntry(MissionTime Time, Observation Observation) : KnowledgeEntry(Time);

/// <summary>A crew member was told an existing observation (by the ground or the other crew member).</summary>
public sealed record TellEntry(MissionTime Time, CrewId Listener, ObservationId Observation) : KnowledgeEntry(Time);

/// <param name="Raised">True when the clue was raised, false when it was cleared.</param>
public sealed record SuspicionEntry(MissionTime Time, ComponentId Component, string Reason, bool Raised) : KnowledgeEntry(Time);

public sealed record RepairEntry(MissionTime Time, ComponentId Component, string RepairId) : KnowledgeEntry(Time);
