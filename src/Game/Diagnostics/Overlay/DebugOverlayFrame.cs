using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Diagnostics.Overlay;

/// <summary>The running mission as the debug overlay shows it (no truth state: ids, time, pacing, hashes and the player's own commands).</summary>
/// <param name="LastHash">The newest 60 s state hash sample, if any.</param>
/// <param name="LiveHash">A hash computed on request ("Hash now") and the MET it was taken at.</param>
public sealed record DebugOverlayMission(
    string MissionId,
    string Difficulty,
    ulong Seed,
    string? RunTag,
    MissionTime Met,
    double CompressionRate,
    string CompressionReason,
    double EffectiveRate,
    bool AdvancingToEvent,
    string SessionFreeze,
    StateHashSample? LastHash,
    int HashSamples,
    StateHashSample? LiveHash,
    IReadOnlyList<CommandLogEntry> Commands);

/// <summary>Everything one refresh of the overlay shows.</summary>
/// <param name="ProcessMs">Godot's process-time monitor for the last frame.</param>
/// <param name="Notice">The latest overlay or crash-reporter message (a saved repro path, a copy confirmation).</param>
public sealed record DebugOverlayFrame(
    string Version,
    string Screen,
    string AppFreeze,
    string Backend,
    double Fps,
    double FrameMs,
    double ProcessMs,
    DebugOverlayMission? Mission,
    string? Notice);
