using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>An engine choice in the planner (OAMS translation, Lodestar engine when docked, retro sequence).</summary>
/// <param name="Engine">
/// The engine as the simulation will fire it (true propellant included); null when it cannot be used at all right now.
/// The planner executes with it but never previews or displays its true propellant (X8).
/// </param>
/// <param name="UnavailableReason">Shown when <paramref name="Available"/> is false, e.g. <c>needs the Agena docked</c>.</param>
/// <param name="Propellant">
/// What the ground knows of this engine's propellant (reported reading, stale reading or coarse downlink band). Previews
/// run on it and the panel shows its state; null means <see cref="PropellantKnowledge.Unknown"/>. Ignored for engines
/// with a fixed load (the solid retro sequence), whose propellant is a design value.
/// </param>
public sealed record PlannerEngineOption(
    string Id, string Label, BurnEngine? Engine, bool Available = true, string? UnavailableReason = null, PropellantKnowledge? Propellant = null)
{
    public const string OamsTranslation = "oams_translation";
    public const string LodestarEngine = "lodestar_engine";
    public const string RetroSequence = "retro_sequence";

    public bool HasFixedDuration => Engine?.FixedDurationS is not null;

    /// <summary>The knowledge the planner works from; null when the engine's propellant is a design value.</summary>
    public PropellantKnowledge? KnownPropellant => Engine is ConstantThrustEngine ? Propellant ?? PropellantKnowledge.Unknown : null;
}
