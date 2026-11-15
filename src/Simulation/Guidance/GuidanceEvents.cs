using Usa.Simulation.Kernel;

namespace Usa.Simulation.Guidance;

/// <summary>A tip card should show (or replace the current one). The text is the authored fallback the host may rephrase.</summary>
/// <param name="Requested">True when the player asked for a stronger hint.</param>
public sealed record TipAvailable(
    MissionTime Time,
    string ProblemId,
    HintStage Stage,
    string Text,
    IReadOnlyList<FactId> FactRefs,
    IReadOnlyList<ActionId> ActionRefs,
    bool Requested) : ISimEvent;

public enum TipWithdrawReason
{
    Resolved,
    Dismissed,
}

/// <summary>The tip card for <paramref name="ProblemId"/> should close.</summary>
public sealed record TipWithdrawn(MissionTime Time, string ProblemId, TipWithdrawReason Reason) : ISimEvent;

public enum ProblemStatus
{
    Inactive,
    Open,
    Retired,
}
