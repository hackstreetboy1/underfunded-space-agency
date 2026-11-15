using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>Vehicle conditions, as the ground knows them, that the planner warns about.</summary>
/// <param name="AssumedAttitudeErrorDeg">Pointing error the preview assumes (e.g. when attitude hold is unavailable).</param>
/// <param name="Hold">What the ground knows of the attitude hold, for the direction label (<c>Retrograde · RCS B hold</c>); null when it knows nothing.</param>
public sealed record PlannerConditions(bool AttitudeControlAvailable = true, double AssumedAttitudeErrorDeg = 0, AttitudeHold? Hold = null)
{
    public static PlannerConditions Nominal { get; } = new();
}

/// <summary>
/// The attitude hold as the ground knows it: which system holds, learned from a crew report or a confirmed command,
/// never read from the true attitude mode.
/// </summary>
/// <param name="SystemLabel">e.g. <c>RCS B</c>.</param>
/// <param name="Source">How the ground knows, e.g. <c>Brandt</c> or <c>crew report</c>.</param>
/// <param name="At">MET the ground learned it.</param>
public sealed record AttitudeHold(string SystemLabel, string Source, MissionTime At);
