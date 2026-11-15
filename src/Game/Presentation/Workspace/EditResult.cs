using Usa.Simulation.Engineering;

namespace Usa.Game.Presentation.Workspace;

public enum EditProblem
{
    None,
    NotOpen,

    /// <summary>The design was assigned to the crew; the board is read-only.</summary>
    Locked,
    OutsideBoard,
    Overlaps,
    NoneAvailable,

    /// <summary>The assembly graph refused the edit; see <see cref="EditResult.Failure"/>.</summary>
    Refused,
    NothingToUndo,
    NothingToRedo,

    /// <summary>No drag or wire gesture is in progress.</summary>
    NoGesture,
}

/// <param name="Message">Plain-language reason for the player; empty on success.</param>
public readonly record struct EditResult(EditProblem Problem, AssemblyFailure Failure, string Message)
{
    public static readonly EditResult Ok = new(EditProblem.None, AssemblyFailure.None, "");

    public bool Succeeded => Problem == EditProblem.None;
}
