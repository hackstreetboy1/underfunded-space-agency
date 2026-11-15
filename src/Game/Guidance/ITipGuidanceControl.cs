namespace Usa.Game.Guidance;

/// <summary>
/// The player's tip card controls, passed through to the simulation's <c>GuidanceDirector</c>. The director answers
/// with <c>TipWithdrawn</c> / <c>TipAvailable</c> events, which reach <see cref="TipService.Observe"/>.
/// </summary>
public interface ITipGuidanceControl
{
    /// <returns>False when no tip is on the card.</returns>
    bool Dismiss();

    /// <returns>False when no problem is open.</returns>
    bool RequestStrongerHint();
}
