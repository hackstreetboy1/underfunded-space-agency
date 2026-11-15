namespace Usa.Game.Presentation.Spacecraft;

/// <summary>Visible chute state (MODELING 7). Vehicle state words map as stowed → Stowed, deployed → Open, cut → Released.</summary>
public enum ChuteDeployState
{
    Stowed,
    Deploying,
    Reefed,
    Open,
    Released,
}
