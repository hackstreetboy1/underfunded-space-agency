using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>A session-handled effect could not take place (e.g. Flight refused to dock); the crew explains <paramref name="ReasonCode"/>.</summary>
public sealed record SessionEffectRefused(MissionTime Time, string Target, string ReasonCode) : ISimEvent;

/// <summary>A crew member's status report: what they know, for the crew voice (meta command <c>status_report</c>).</summary>
public sealed record StatusReportReady(MissionTime Time, CommandId Origin, CrewId Crew, IReadOnlyList<FactId> KnownFacts) : ISimEvent;

/// <summary>A state hash was recorded (every 60 s MET) for determinism checks and replays.</summary>
public sealed record StateHashRecorded(MissionTime Time, string Hash) : ISimEvent;

/// <summary>
/// A ready repair design lost reserved parts to an effect and is no longer ready (<c>session.repair.&lt;id&gt;.ready</c> reads
/// false); its other parts went back to the stock. Registering it again reserves afresh if the parts are there.
/// </summary>
/// <param name="AssemblyId">The design's registration id.</param>
/// <param name="Part">The part the effect took.</param>
public sealed record RepairReservationLost(MissionTime Time, string AssemblyId, PartId Part) : ISimEvent;
