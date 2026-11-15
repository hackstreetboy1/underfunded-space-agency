using Usa.Game.Content;
using Usa.Game.Services.Saves;

namespace Usa.Game.Flow;

/// <summary>What the player picked on mission select; <see cref="Seed"/> comes from <see cref="SeedPlanner.Initial"/>.</summary>
public sealed record MissionSelection(string MissionId, string Difficulty, ulong Seed);

/// <summary>One request to build and run a mission session.</summary>
/// <param name="RunNumber">Increases with every launch in this process (restarts included); the profile records each run once.</param>
/// <param name="Seed">Scenario seed; null when resuming, because the resume payload holds it.</param>
/// <param name="Resume">The save to continue from, or null for a fresh run.</param>
public sealed record MissionLaunch(long RunNumber, string MissionId, string Difficulty, ulong? Seed, bool GuidedFirstRun, ResumeSave? Resume);

/// <summary>A mission that ended by recovery or loss of the whole crew (D10), ready for the debrief.</summary>
/// <param name="Seed">The seed the session actually ran with (reported by <see cref="ScreenRouter.LoadingCompleted"/>).</param>
public sealed record CompletedMission(MissionLaunch Launch, ulong Seed, DebriefView View);

public sealed record ScreenChange(Screen From, Screen To);

/// <summary>An action the current state does not allow; the state is unchanged.</summary>
public sealed record FlowRejection(string Action, Screen Screen, string Reason);

/// <summary>A flight that ended without a debrief (<see cref="ScreenRouter.MissionAborted"/>); the main menu tells the player.</summary>
public sealed record MissionAbort(MissionLaunch Launch, MissionAbortReason Reason);
